using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — transition runner: lifecycle callbacks
/// (sync and async), queued page-scope disposal, events and error propagation.
/// </summary>
public sealed partial class NavigationService
{
    /// <summary>
    /// How long disposal gating waits for an entry's in-flight async lifecycle to
    /// complete before releasing its page scope anyway. Guards against a ViewModel
    /// that ignores its cancellation token blocking navigation forever.
    /// </summary>
    private static readonly TimeSpan DisposalGateTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Raises <see cref="RegionNavigated"/> subscriber-by-subscriber, so one throwing
    /// subscriber cannot prevent the others from receiving the event.
    /// </summary>
    private void RaiseRegionNavigated(string regionName, INavigationViewModel viewModel)
    {
        var handlers = RegionNavigated;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string, INavigationViewModel>)handler)(regionName, viewModel);
            }
            catch (Exception ex)
            {
                try { SubscriberError?.Invoke(ex); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Raises <see cref="ViewModelReleased"/> subscriber-by-subscriber.
    /// </summary>
    private void RaiseViewModelReleased(string regionName, INavigationViewModel viewModel)
    {
        var handlers = ViewModelReleased;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string, INavigationViewModel>)handler)(regionName, viewModel);
            }
            catch (Exception ex)
            {
                try { SubscriberError?.Invoke(ex); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Raises <see cref="RegionCacheCleared"/> subscriber-by-subscriber.
    /// </summary>
    private void RaiseRegionCacheCleared(string regionName)
    {
        var handlers = RegionCacheCleared;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string>)handler)(regionName);
            }
            catch (Exception ex)
            {
                try { SubscriberError?.Invoke(ex); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Invokes queued event handlers. Subscriber exceptions are swallowed so a faulty
    /// subscriber can never corrupt navigation state; they are reported via
    /// <paramref name="onError"/> for diagnostics.
    /// </summary>
    private static void RaiseEvents(List<Action>? events, Action<Exception>? onError)
    {
        if (events is null or { Count: 0 }) return;
        foreach (var action in events)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Swallow: event subscribers must never break navigation.
                // Report for diagnostics; the reporter itself must not throw.
                try { onError?.Invoke(ex); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Executes the work items collected under the navigation lock: lifecycle callbacks
    /// (sync, then async), page-scope disposals and activation, then queued events.
    /// Lifecycle/disposal exceptions are collected and rethrown afterwards so one failing
    /// page never aborts the rest.
    /// </summary>
    private async Task RunTransitionAsync(TransitionWork work)
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

        RaiseEvents(work.Events, ex => SubscriberError?.Invoke(ex));
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
    /// <param name="entry">The stack entry being deactivated.</param>
    /// <param name="destroy">True when the entry's page scope should be disposed.</param>
    /// <param name="notifyLifecycle">
    /// True when the entry was the active page: its <c>OnNavigatedFrom</c> callback runs.
    /// False for entries that were already inactive (covered) — they are disposed without
    /// a second lifecycle notification.
    /// </param>
    /// <param name="work">The transition work list to append to.</param>
    private void QueueDeactivation(NavigationEntry entry, bool destroy, bool notifyLifecycle, TransitionWork work)
    {
        DetachNavigation(entry.ViewModel);

        // The entry is leaving the active position: signal its async lifecycle to stop.
        // Idempotent — a covered entry cancelled here stays cancelled if later disposed.
        try { entry.LifecycleCts.Cancel(); }
        catch (ObjectDisposedException) { }

        var plan = BuildDeactivatePlan(entry, destroy);

        if (notifyLifecycle)
        {
            // A ViewModel implementing INavigationAwareAsync gets the async callbacks
            // instead of the synchronous ones. The token is already cancelled (above);
            // a well-behaved ViewModel returns promptly.
            if (entry.ViewModel is INavigationAwareAsync asyncAware)
            {
                var token = entry.LifecycleCts.Token;
                work.NavigatedFromAsyncActions.Add(async () =>
                {
                    var gateTask = EnterInFlightLifecycle(entry);
                    try
                    {
                        await asyncAware.OnNavigatedFromAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // Expected: the token is already cancelled (the page is leaving).
                    }
                    finally
                    {
                        ExitInFlightLifecycle(entry, gateTask);
                    }
                });
            }
            else if (entry.ViewModel is INavigationAware aware)
            {
                work.NavigatedFromActions.Add(() => aware.OnNavigatedFrom());
            }
        }

        if (!plan.ShouldDispose)
            return;

        work.DisposeActions.Add(async () =>
        {
            // Disposal gating: do not release the page scope while the entry's async
            // lifecycle is still running — it may still touch the scope's services.
            // Wait with a timeout so a ViewModel that ignores cancellation cannot
            // block navigation forever; on timeout the scope is released anyway and
            // the hang is reported via SubscriberError.
            var gateTask = entry.InFlightLifecycleGate?.Task;
            if (gateTask is not null && !gateTask.IsCompleted)
            {
                var completed = await Task.WhenAny(gateTask, Task.Delay(DisposalGateTimeout)).ConfigureAwait(false);
                if (!ReferenceEquals(completed, gateTask))
                {
                    try
                    {
                        SubscriberError?.Invoke(new TimeoutException(
                            $"Disposal gate timed out after {DisposalGateTimeout.TotalSeconds:F0}s waiting for " +
                            $"'{entry.ViewModel.GetType().FullName}' async lifecycle to complete. " +
                            "The page scope is being released anyway; the ViewModel ignored its cancellation token."));
                    }
                    catch { }
                }
            }

            await DisposeScopeAsync(entry.Scope, work.Errors).ConfigureAwait(false);

            using (_stateLock.EnterScope())
                _pendingScopeDisposals.Remove(entry.Scope);

            // The lifecycle CTS is no longer needed once the entry is gone.
            try { entry.LifecycleCts.Dispose(); }
            catch { }

            // Ownership released: notify even if the scope disposal threw (the error
            // was collected above and will be rethrown by RunTransitionAsync).
            foreach (var region in plan.RegionsToNotify)
            {
                var capturedRegion = region;
                work.Events.Add(() => RaiseViewModelReleased(capturedRegion, entry.ViewModel));
            }
        });
    }

    /// <summary>
    /// Marks the start of an async lifecycle callback for the entry. Returns the gate
    /// task that completes when all in-flight callbacks for the entry finish.
    /// </summary>
    private static Task EnterInFlightLifecycle(NavigationEntry entry)
    {
        entry.InFlightLifecycleCount++;
        if (entry.InFlightLifecycleCount == 1)
        {
            entry.InFlightLifecycleGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return entry.InFlightLifecycleGate!.Task;
    }

    /// <summary>
    /// Marks the end of an async lifecycle callback for the entry.
    /// </summary>
    private static void ExitInFlightLifecycle(NavigationEntry entry, Task gateTask)
    {
        entry.InFlightLifecycleCount--;
        if (entry.InFlightLifecycleCount == 0 && ReferenceEquals(entry.InFlightLifecycleGate?.Task, gateTask))
        {
            entry.InFlightLifecycleGate.TrySetResult();
            entry.InFlightLifecycleGate = null;
        }
    }

    /// <summary>
    /// Queues the activation work (OnNavigatedTo callback) for one entry.
    /// A ViewModel implementing <see cref="INavigationAwareAsync"/> gets the async
    /// callback instead of the synchronous one. The async callback receives the
    /// entry's lifecycle token and is tracked as in-flight for disposal gating.
    /// </summary>
    private void QueueActivate(NavigationEntry entry, object? parameter, TransitionWork work)
    {
        var viewModel = entry.ViewModel;
        if (viewModel is INavigationAwareAsync asyncAware)
        {
            work.NavigatedToAsyncActions.Add(async () =>
            {
                var gateTask = EnterInFlightLifecycle(entry);
                try
                {
                    await asyncAware.OnNavigatedToAsync(parameter, entry.LifecycleCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (entry.LifecycleCts.Token.IsCancellationRequested)
                {
                    // Expected: the page left the active position while its init was
                    // still running. Not an error — the navigation itself succeeded.
                }
                finally
                {
                    ExitInFlightLifecycle(entry, gateTask);
                }
            });
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
    private sealed class NavigationEntry
    {
        public NavigationEntry(string regionName, INavigationViewModel viewModel, object? parameter, NavigationMode mode, IServiceScope scope)
        {
            RegionName = regionName;
            ViewModel = viewModel;
            Parameter = parameter;
            Mode = mode;
            Scope = scope;
        }

        public string RegionName { get; }
        public INavigationViewModel ViewModel { get; }
        public object? Parameter { get; set; }
        public NavigationMode Mode { get; }
        public IServiceScope Scope { get; }

        /// <summary>
        /// Cancelled when the entry leaves the active position (covered, popped,
        /// replaced, or cleared). Async lifecycle callbacks receive this token and
        /// should stop using the page's dependencies when it fires.
        /// </summary>
        public CancellationTokenSource LifecycleCts { get; } = new();

        /// <summary>
        /// Number of async lifecycle callbacks currently running for this entry.
        /// Used by disposal gating to avoid releasing the page scope while its
        /// async lifecycle still runs.
        /// </summary>
        public int InFlightLifecycleCount { get; set; }

        /// <summary>
        /// Completes when <see cref="InFlightLifecycleCount"/> drops to zero.
        /// Null when no async lifecycle is in flight.
        /// </summary>
        public TaskCompletionSource? InFlightLifecycleGate { get; set; }
    }
}
