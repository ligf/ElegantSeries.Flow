using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — transition runner: lifecycle callbacks
/// (sync and async), queued page-scope disposal, events and error propagation.
/// </summary>
public sealed partial class NavigationService
{
    /// <summary>
    /// Invokes queued event handlers. Subscriber exceptions are swallowed so a faulty
    /// subscriber can never corrupt navigation state.
    /// </summary>
    private static void RaiseEvents(List<Action>? events)
    {
        if (events is null or { Count: 0 }) return;
        foreach (var action in events)
        {
            try
            {
                action();
            }
            catch
            {
                // Swallow: event subscribers must never break navigation.
            }
        }
    }

    /// <summary>
    /// Executes the work items collected under the navigation lock: lifecycle callbacks
    /// (sync, then async), page-scope disposals and activation, then queued events.
    /// Lifecycle/disposal exceptions are collected and rethrown afterwards so one failing
    /// page never aborts the rest.
    /// </summary>
    private static async Task RunTransitionAsync(TransitionWork work)
    {
        foreach (var action in work.NavigatedFromActions)
            SafeLifecycle(action, work.Errors);

        foreach (var action in work.NavigatedFromAsyncActions)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) { work.Errors.Add(ex); }
        }

        foreach (var dispose in work.DisposeActions)
        {
            try { await dispose().ConfigureAwait(false); }
            catch (Exception ex) { work.Errors.Add(ex); }
        }

        foreach (var action in work.NavigatedToActions)
            SafeLifecycle(action, work.Errors);

        foreach (var action in work.NavigatedToAsyncActions)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) { work.Errors.Add(ex); }
        }

        RaiseEvents(work.Events);
        ThrowCollectedErrors(work.Errors);
    }

    /// <summary>
    /// Runs a lifecycle callback and collects any exception instead of propagating it.
    /// </summary>
    private static void SafeLifecycle(Action action, List<Exception> errors)
    {
        try { action(); }
        catch (Exception ex) { errors.Add(ex); }
    }

    /// <summary>
    /// Queues the deactivation work (OnNavigatedFrom callback plus page-scope disposal
    /// when needed) for one entry. Must be called while holding
    /// <see cref="_navigationLock"/> but not <see cref="_stateLock"/>.
    /// </summary>
    private void QueueDeactivation(NavigationEntry entry, bool destroy, TransitionWork work)
    {
        DetachNavigation(entry.ViewModel);
        var plan = BuildDeactivatePlan(entry, destroy);

        // A ViewModel implementing INavigationAwareAsync gets the async callbacks
        // instead of the synchronous ones.
        if (entry.ViewModel is INavigationAwareAsync asyncAware)
        {
            work.NavigatedFromAsyncActions.Add(() => asyncAware.OnNavigatedFromAsync(CancellationToken.None));
        }
        else if (entry.ViewModel is INavigationAware aware)
        {
            work.NavigatedFromActions.Add(() => aware.OnNavigatedFrom());
        }

        if (!plan.ShouldDispose)
            return;

        work.DisposeActions.Add(async () =>
        {
            await DisposeScopeAsync(entry.Scope, work.Errors).ConfigureAwait(false);

            using (_stateLock.EnterScope())
                _pendingScopeDisposals.Remove(entry.Scope);

            // Ownership released: notify even if the scope disposal threw (the error
            // was collected above and will be rethrown by RunTransitionAsync).
            foreach (var region in plan.RegionsToNotify)
            {
                var capturedRegion = region;
                work.Events.Add(() => ViewModelDisposed?.Invoke(capturedRegion, entry.ViewModel));
            }
        });
    }

    /// <summary>
    /// Queues the activation work (OnNavigatedTo callback) for one ViewModel.
    /// A ViewModel implementing <see cref="INavigationAwareAsync"/> gets the async
    /// callback instead of the synchronous one.
    /// </summary>
    private static void QueueActivate(INavigationViewModel viewModel, object? parameter, TransitionWork work)
    {
        if (viewModel is INavigationAwareAsync asyncAware)
        {
            work.NavigatedToAsyncActions.Add(() => asyncAware.OnNavigatedToAsync(parameter, CancellationToken.None));
        }
        else if (viewModel is INavigationAware aware)
        {
            work.NavigatedToActions.Add(() => aware.OnNavigatedTo(parameter));
        }
    }

    /// <summary>
    /// Builds the deactivation plan for an entry that is leaving the active slot:
    /// whether its page scope should be disposed and which regions to notify.
    /// Acquires <see cref="_stateLock"/> internally.
    /// </summary>
    private DeactivatePlan BuildDeactivatePlan(NavigationEntry entry, bool destroy)
    {
        var regionsToNotify = new HashSet<string> { entry.RegionName };
        bool shouldDispose;

        using (_stateLock.EnterScope())
        {
            if (_pendingScopeDisposals.TryGetValue(entry.Scope, out var pending))
            {
                regionsToNotify.UnionWith(pending.Regions);
            }

            if (IsScopeReferencedLocked(entry.Scope))
            {
                // Still needed by another stack entry or the cache: defer disposal.
                shouldDispose = false;
                if (destroy)
                {
                    // Defensive: a destroy request for a still-referenced scope is remembered
                    // so the last pop disposes it.
                    if (_pendingScopeDisposals.TryGetValue(entry.Scope, out var existing))
                    {
                        existing.Regions.UnionWith(regionsToNotify);
                    }
                    else
                    {
                        _pendingScopeDisposals[entry.Scope] =
                            new PendingDisposal(entry.ViewModel, new HashSet<string>(regionsToNotify));
                    }
                }
            }
            else
            {
                shouldDispose = destroy || _pendingScopeDisposals.ContainsKey(entry.Scope);
            }
        }

        return new DeactivatePlan(shouldDispose, [.. regionsToNotify]);
    }

    /// <summary>
    /// Clears the navigation service reference of a ViewModel that is no longer active.
    /// </summary>
    private static void DetachNavigation(INavigationViewModel viewModel)
        => viewModel.Navigation = null;

    /// <summary>
    /// Attaches this navigation service to the newly activated ViewModel.
    /// </summary>
    private void AttachNavigation(INavigationViewModel viewModel)
        => viewModel.Navigation = this;

    // ────────────────────── Nested types ──────────────────────

    /// <summary>Work items collected under the navigation lock and executed outside of it.</summary>
    private sealed class TransitionWork
    {
        public List<Action> NavigatedFromActions { get; } = [];

        public List<Func<Task>> NavigatedFromAsyncActions { get; } = [];

        // Func<Task> is used for dispose actions to avoid subtle ValueTask conversion issues.
        public List<Func<Task>> DisposeActions { get; } = [];

        public List<Action> NavigatedToActions { get; } = [];

        public List<Func<Task>> NavigatedToAsyncActions { get; } = [];

        public List<Action> Events { get; } = [];

        public List<Exception> Errors { get; } = [];
    }

    /// <summary>Deactivation plan for one entry: whether to dispose its page scope and which regions to notify.</summary>
    private readonly record struct DeactivatePlan(bool ShouldDispose, string[] RegionsToNotify);

    /// <summary>One page on a region's navigation stack. The scope owns the ViewModel's lifetime.</summary>
    private sealed record NavigationEntry(string RegionName, INavigationViewModel ViewModel, object? Parameter, NavigationMode Mode, IServiceScope Scope);
}
