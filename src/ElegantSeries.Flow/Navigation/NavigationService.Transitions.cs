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
    private TimeSpan DisposalGateTimeout => _options.DisposalGateTimeout;

    /// <summary>
    /// Tracks the entry whose async lifecycle callback is running on the current
    /// async flow. Used by disposal gating to avoid self-wait: a reentrant
    /// navigation issued from inside a callback must not wait for that same
    /// callback to finish.
    /// </summary>
    private static readonly AsyncLocal<NavigationEntry?> _currentLifecycleEntry = new();

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

        // The entry is leaving the active position: its token must be cancelled.
        // Deferred until after the navigation lock is released: Cancel() invokes
        // user callbacks synchronously which may reenter the service (deadlock
        // if called while holding the lock). Idempotent — a covered entry
        // cancelled here stays cancelled if later disposed.
        work.CtsToCancel.Add(entry.LifecycleCts);

        var plan = BuildDeactivatePlan(entry, destroy);

        if (notifyLifecycle)
        {
            // A ViewModel implementing INavigationAwareAsync gets the async callbacks
            // instead of the synchronous ones. The token is already cancelled (above);
            // a well-behaved ViewModel returns promptly.
            if (entry.ViewModel is INavigationAwareAsync asyncAware)
            {
                var token = entry.LifecycleCts.Token;
                var gateTask = entry.EnterInFlightLifecycle();
                work.NavigatedFromAsyncActions.Add(async () =>
                {
                    var previousCurrent = _currentLifecycleEntry.Value;
                    _currentLifecycleEntry.Value = entry;
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
                        _currentLifecycleEntry.Value = previousCurrent;
                        entry.ExitInFlightLifecycle(gateTask);
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
            //
            // Self-dispose handling: if this disposal runs inside the entry's own
            // lifecycle callback (reentrant navigation), waiting for the gate
            // would deadlock until the timeout, AND disposing immediately would
            // pull the scope's services out from under the still-running callback.
            // Defer the disposal until the callback exits.
            bool isSelfDispose = ReferenceEquals(_currentLifecycleEntry.Value, entry);
            var gateTask = entry.InFlightGateTask;
            if (gateTask is not null && !gateTask.IsCompleted && !isSelfDispose)
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

            if (isSelfDispose)
            {
                // The callback is still on this async flow and may touch the
                // scope's services after the navigation returns. Defer the disposal
                // until the in-flight callbacks exit; tracked at service level so
                // DisposeAsync can wait for it.
                var gate = entry.InFlightGateTask;
                if (gate is not null && !gate.IsCompleted)
                {
                    TrackDeferredDisposal(gate, entry.Scope,
                        ex => { try { SubscriberError?.Invoke(ex); } catch { } });
                }
                else
                {
                    // No callback in flight (shouldn't happen for self-dispose,
                    // but be safe): dispose inline.
                    await DisposeScopeAsync(entry.Scope, work.Errors).ConfigureAwait(false);
                }
                // The scope stays in _pendingScopeDisposals until the deferred
                // disposal runs; TrackDeferredDisposal removes it afterwards.
                // (Removal is handled by the deferred task's continuation.)
            }
            else
            {
                await DisposeScopeAsync(entry.Scope, work.Errors).ConfigureAwait(false);

                using (_stateLock.EnterScope())
                    _pendingScopeDisposals.Remove(entry.Scope);
            }

            // The lifecycle CTS is no longer needed once the entry is gone —
            // except for self-dispose, where the running callback still holds
            // the token. It will be retired and disposed when the in-flight
            // count drops to zero (see BeginNewActivation/ExitInFlightLifecycle).
            if (!isSelfDispose)
            {
                try { entry.LifecycleCts.Dispose(); }
                catch { }
            }

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
    /// Queues the activation work (OnNavigatedTo callback) for one entry.
    /// A ViewModel implementing <see cref="INavigationAwareAsync"/> gets the async
    /// callback instead of the synchronous one. The async callback receives the
    /// entry's lifecycle token and is tracked as in-flight for disposal gating.
    /// Must be called while holding <see cref="_navigationLock"/>: the in-flight
    /// registration happens synchronously at queue time so a concurrent disposal
    /// cannot miss it.
    /// </summary>
    private void QueueActivate(NavigationEntry entry, object? parameter, TransitionWork work)
    {
        var viewModel = entry.ViewModel;
        if (viewModel is INavigationAwareAsync asyncAware)
        {
            // Register in-flight SYNCHRONOUSLY at queue time (we hold _navigationLock):
            // a concurrent disposal starting after this point must observe it.
            var gateTask = entry.EnterInFlightLifecycle();
            var token = entry.LifecycleCts.Token;
            work.NavigatedToAsyncActions.Add(async () =>
            {
                // Mark the entry whose callback is running: a reentrant navigation
                // that disposes this same entry must not wait for itself.
                var previousCurrent = _currentLifecycleEntry.Value;
                _currentLifecycleEntry.Value = entry;
                try
                {
                    await asyncAware.OnNavigatedToAsync(parameter, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Expected: the page left the active position while its init was
                    // still running. Not an error — the navigation itself succeeded.
                }
                finally
                {
                    _currentLifecycleEntry.Value = previousCurrent;
                    entry.ExitInFlightLifecycle(gateTask);
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

        /// <summary>
        /// Cancellation token sources to cancel after the navigation lock is
        /// released. Cancel() invokes user callbacks synchronously and must not
        /// run while holding locks (deadlock risk).
        /// </summary>
        public List<CancellationTokenSource> CtsToCancel { get; } = [];

        /// <summary>
        /// Cancels all queued CTSs outside of locks. Exceptions from user
        /// callbacks are swallowed; the navigation state was already committed.
        /// </summary>
        public void CancelQueuedCts()
        {
            foreach (var cts in CtsToCancel)
            {
                NavigationEntry.CancelOutsideLocks(cts);
            }
            CtsToCancel.Clear();
        }
    }

    /// <summary>Deactivation plan for one entry: whether to dispose its page scope and which regions to notify.</summary>
    private readonly record struct DeactivatePlan(bool ShouldDispose, string[] RegionsToNotify);

    /// <summary>One page on a region's navigation stack. The scope owns the ViewModel's lifetime.</summary>
    private sealed class NavigationEntry
    {
        private readonly object _inFlightLock = new();
        private int _inFlightCount;
        private TaskCompletionSource? _inFlightGate;
        private List<CancellationTokenSource> _retiredCts = new();

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
        public CancellationTokenSource LifecycleCts { get; private set; } = new();

        /// <summary>
        /// Starts a new activation cycle for this entry (e.g. GoBack reactivation
        /// or refresh). Replaces <see cref="LifecycleCts"/> with a fresh source and
        /// retires the old one. The old source is NOT cancelled or disposed here —
        /// <see cref="CancellationTokenSource"/> synchronously invokes user
        /// callbacks which may reenter the navigation service; calling it while
        /// holding the navigation/state locks risks deadlock. The caller must
        /// cancel the returned source after releasing all locks via
        /// <see cref="CancelOutsideLocks"/>.
        /// In-flight callbacks from the previous cycle keep running under their own
        /// token and remain tracked. Retired sources are disposed when the
        /// in-flight count drops to zero.
        /// </summary>
        /// <returns>The retired cancellation token source, to be cancelled by the
        /// caller after releasing locks.</returns>
        public CancellationTokenSource BeginNewActivation()
        {
            lock (_inFlightLock)
            {
                var oldCts = LifecycleCts;
                LifecycleCts = new CancellationTokenSource();
                _retiredCts.Add(oldCts);
                return oldCts;
            }
        }

        /// <summary>
        /// Cancels a retired CTS outside of any navigation locks. User cancellation
        /// callbacks run synchronously inside <c>Cancel()</c> and may throw or
        /// reenter; exceptions are swallowed to avoid corrupting the
        /// already-committed navigation state.
        /// </summary>
        public static void CancelOutsideLocks(CancellationTokenSource cts)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            catch
            {
                // User cancellation callbacks must not break navigation.
                // The navigation state was already committed before this ran.
            }
        }

        /// <summary>
        /// Registers an async lifecycle callback as in-flight. Must be called when the
        /// callback is QUEUED (while holding the navigation lock), not when it starts
        /// executing — otherwise a concurrent disposal could miss it.
        /// </summary>
        /// <returns>The gate task completing when all in-flight callbacks finish.</returns>
        public Task EnterInFlightLifecycle()
        {
            lock (_inFlightLock)
            {
                _inFlightCount++;
                if (_inFlightCount == 1)
                {
                    _inFlightGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return _inFlightGate!.Task;
            }
        }

        /// <summary>
        /// Unregisters an async lifecycle callback. When the last in-flight
        /// callback exits, retired cancellation sources are disposed and any
        /// deferred scope disposals (from reentrant self-destroying navigation)
        /// are executed.
        /// </summary>
        public void ExitInFlightLifecycle(Task gateTask)
        {
            List<CancellationTokenSource>? retiredToDispose = null;
            lock (_inFlightLock)
            {
                _inFlightCount--;
                if (_inFlightCount == 0 && _inFlightGate is not null && ReferenceEquals(_inFlightGate.Task, gateTask))
                {
                    _inFlightGate.TrySetResult();
                    _inFlightGate = null;
                }
                if (_inFlightCount == 0 && _retiredCts.Count > 0)
                {
                    retiredToDispose = _retiredCts;
                    _retiredCts = new();
                }
            }

            // Dispose outside the lock: user code may run during disposal.
            if (retiredToDispose is not null)
            {
                foreach (var cts in retiredToDispose)
                {
                    try { cts.Dispose(); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// The gate task completing when all in-flight callbacks finish, or
        /// <see langword="null"/> when none is in flight. For disposal gating.
        /// </summary>
        public Task? InFlightGateTask
        {
            get
            {
                lock (_inFlightLock)
                {
                    return _inFlightGate?.Task;
                }
            }
        }
    }
}
