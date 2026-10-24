using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — lifecycle callback runners (invoked directly by
/// the per-region window pump), queued page-scope disposal, events and error propagation.
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
    /// Tracks the entry whose lifecycle processing is running on the current
    /// async flow: its To/From callbacks, and its leave guard (which runs as
    /// part of the entry's departure). Used by disposal gating to avoid
    /// self-wait: a disposal issued from inside the entry's own processing must
    /// not wait for that same processing to finish. Also identifies navigation
    /// calls made from within that processing: they register an intent into the
    /// window's slot and return immediately, instead of awaiting a fate the
    /// pump can only complete after the processing returns.
    /// </summary>
    /// <remarks>
    /// The slot holds a per-invocation <see cref="LifecycleMarker"/>, not the
    /// entry itself: an async flow forked from inside a callback captures the
    /// slot's value in its execution-context snapshot, and the callback's
    /// restore (a write on the callback's own flow) never reaches the fork.
    /// Expiring the shared marker when the invocation returns is what stops
    /// the fork classifying as in-callback for the rest of its life.
    /// </remarks>
    private static readonly AsyncLocal<LifecycleMarker?> _currentLifecycleMarker = new();

    /// <summary>
    /// One lifecycle invocation's marker. A fresh instance is created for
    /// every guard/To/From invocation and expired the moment that invocation
    /// returns — disposal callbacks that outlive their invocation, and flows
    /// forked from inside it, observe <see cref="Active"/> as false.
    /// </summary>
    private sealed class LifecycleMarker(NavigationEntry entry)
    {
        public NavigationEntry Entry { get; } = entry;

        /// <summary>True while the invocation that created this marker is still running.</summary>
        public volatile bool Active = true;
    }

    /// <summary>
    /// The lifecycle entry whose processing is running on this flow, when it
    /// belongs to THIS service instance and its invocation is still running;
    /// null otherwise. Calls into another instance from this instance's
    /// callback are outside calls for that instance and must not be classified
    /// as in-callback there.
    /// </summary>
    private NavigationEntry? CurrentOwnLifecycleEntry =>
        _currentLifecycleMarker.Value is { Active: true } marker
            && ReferenceEquals(marker.Entry.OwnerService, this)
            ? marker.Entry
            : null;

    /// <summary>
    /// Fires a ViewModel's Navigation PropertyChanged notification with the
    /// lifecycle-entry marker cleared, as in <see cref="RaiseRegionNavigated"/>:
    /// notification subscribers are not lifecycle callbacks and must not be
    /// classified as such, whatever flow raises the notification.
    /// </summary>
    private static void RaiseNavigationChangedNotification(INavigationViewModel viewModel)
    {
        var previousCurrent = _currentLifecycleMarker.Value;
        _currentLifecycleMarker.Value = null;
        try
        {
            viewModel.RaiseNavigationChanged();
        }
        finally
        {
            _currentLifecycleMarker.Value = previousCurrent;
        }
    }

    /// <summary>
    /// Reports an error to <see cref="SubscriberError"/> subscribers with the
    /// lifecycle-entry marker cleared, as in <see cref="RaiseRegionNavigated"/>:
    /// an error handler is not a lifecycle callback (one that routes to an
    /// error page must be treated as an outside caller, and a
    /// <c>ClearCache</c> call from it must not trip the in-callback guard).
    /// The report itself never throws.
    /// </summary>
    private void ReportSubscriberError(Exception error)
    {
        var handlers = SubscriberError;
        if (handlers is null)
        {
            return;
        }

        var previousCurrent = _currentLifecycleMarker.Value;
        _currentLifecycleMarker.Value = null;
        try
        {
            handlers.Invoke(error);
        }
        catch
        {
            // A faulty error handler must never break navigation.
        }
        finally
        {
            _currentLifecycleMarker.Value = previousCurrent;
        }
    }

    /// <summary>
    /// Raises <see cref="RegionNavigated"/> subscriber-by-subscriber, so one throwing
    /// subscriber cannot prevent the others from receiving the event. Subscribers
    /// are invoked with the lifecycle-entry marker cleared: an event handler is
    /// not a lifecycle callback, and when the raising flow happens to carry a
    /// marker (a pump started from inside a callback inherits that callback's
    /// entry as its baseline), a navigation issued by the handler would
    /// otherwise be misclassified as issued from within that long-finished
    /// callback — completed immediately instead of awaited, and counted toward
    /// the chain limit.
    /// </summary>
    private void RaiseRegionNavigated(string regionName, INavigationViewModel viewModel)
    {
        var handlers = RegionNavigated;
        if (handlers is null)
        {
            return;
        }

        var previousCurrent = _currentLifecycleMarker.Value;
        _currentLifecycleMarker.Value = null;
        try
        {
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
        finally
        {
            _currentLifecycleMarker.Value = previousCurrent;
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

        // Marker cleared for subscribers, as in RaiseRegionNavigated.
        var previousCurrent = _currentLifecycleMarker.Value;
        _currentLifecycleMarker.Value = null;
        try
        {
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
        finally
        {
            _currentLifecycleMarker.Value = previousCurrent;
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

        // Marker cleared for subscribers, as in RaiseRegionNavigated.
        var previousCurrent = _currentLifecycleMarker.Value;
        _currentLifecycleMarker.Value = null;
        try
        {
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
        finally
        {
            _currentLifecycleMarker.Value = previousCurrent;
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

    // ────────────────────── Lifecycle callback runners ──────────────────────

    /// <summary>
    /// Appends a lifecycle callback exception to a shared error list. The list
    /// itself is the lock object: after a disposal-gate timeout the pump reads
    /// (snapshots) the list while the abandoned runner may still be appending.
    /// </summary>
    private static void CollectError(List<Exception> errors, Exception ex)
    {
        lock (errors)
        {
            errors.Add(ex);
        }
    }

    /// <summary>
    /// Appends a lifecycle callback exception to a shared error list, first
    /// stripping cancellation wrappers (and flattening aggregates, including
    /// aggregates nested inside a cancellation wrapper) so the collected
    /// errors are the real errors, not their wrappers — the same rule the
    /// guard paths apply. A failure carrying no real error at all (a pure
    /// cancellation, or an aggregate of only cancellations) keeps the
    /// original exception, preserving the previous collection behavior
    /// for that case.
    /// </summary>
    /// <remarks>
    /// The computed leaves are appended under a single acquisition of the
    /// list lock: the pump snapshots these lists under the same lock after
    /// a disposal-gate timeout, and appending leaf-by-leaf would let a
    /// snapshot observe a strict prefix while the remaining leaves land in
    /// a list nobody re-reads.
    /// </remarks>
    private static void CollectErrors(List<Exception> errors, Exception ex)
    {
        if (!CollectRealErrors(errors, ex))
        {
            lock (errors)
            {
                errors.Add(ex);
            }
        }
    }

    /// <summary>
    /// Appends only the real errors carried by <paramref name="ex"/> (after
    /// unwrapping and flattening), or nothing when it carries none.
    /// Returns <see langword="true"/> when at least one real error was
    /// appended. Unlike <see cref="CollectErrors"/>, this never falls back
    /// to the original exception: the cancellation-filter branches use it
    /// so a pure-cancellation shape — for example an OCE wrapping an
    /// aggregate of only OCEs — stays swallowed instead of surfacing as a
    /// collected error via the fallback.
    /// </summary>
    private static bool CollectRealErrors(List<Exception> errors, Exception ex)
    {
        var toAdd = new List<Exception>();
        CollectInto(toAdd, ex);
        if (toAdd.Count == 0)
        {
            return false;
        }

        lock (errors)
        {
            errors.AddRange(toAdd);
        }

        return true;
    }

    private static void CollectInto(List<Exception> toAdd, Exception ex)
    {
        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                CollectInto(toAdd, inner);
            }

            return;
        }

        if (FirstNonCancellationError(ex) is { } real)
        {
            if (real is AggregateException realAggregate)
            {
                foreach (var inner in realAggregate.Flatten().InnerExceptions)
                {
                    CollectInto(toAdd, inner);
                }
            }
            else
            {
                toAdd.Add(real);
            }
        }
    }

    /// <summary>
    /// Runs the entry's <c>OnNavigatedFrom</c> callback (the async variant when
    /// implemented, otherwise the synchronous one) and collects any exception into
    /// <paramref name="errors"/> instead of propagating it. The entry's lifecycle
    /// token has already been cancelled by the caller. The caller registered
    /// the callback as in-flight under the navigation lock before invoking
    /// this runner and passes that registration as <paramref name="gateTask"/>;
    /// it is exited here when the callback finishes, so a concurrent disposal
    /// always observes the running callback. Called by the region's pump
    /// while holding no locks.
    /// </summary>
    private async Task RunFromCallbacksAsync(NavigationEntry entry, Task gateTask, List<Exception> errors)
    {
        if (entry.ViewModel is INavigationAwareAsync asyncAware)
        {
            var token = entry.LifecycleCts.Token;
            var marker = new LifecycleMarker(entry);
            var previousCurrent = _currentLifecycleMarker.Value;
            _currentLifecycleMarker.Value = marker;
            try
            {
                await asyncAware.OnNavigatedFromAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (token.IsCancellationRequested)
            {
                // Expected: the token is already cancelled (the page is leaving).
                // But a cancellation wrapper around a real error is not a
                // cancellation outcome: collect the wrapped error, and swallow
                // only a pure cancellation (bare OCE or an OCE-only chain).
                // The wrapped error may itself be an aggregate, so it goes
                // through the real-leaf collector rather than a single
                // append — but without the fallback: if the wrapper carries
                // no real error at all, the outcome stays swallowed.
                if (FirstNonCancellationError(ex) is { } real)
                {
                    CollectRealErrors(errors, real);
                }
            }
            catch (Exception ex)
            {
                CollectErrors(errors, ex);
            }
            finally
            {
                marker.Active = false;
                _currentLifecycleMarker.Value = previousCurrent;
                entry.ExitInFlightLifecycle(gateTask);
            }
        }
        else if (entry.ViewModel is INavigationAware aware)
        {
            var marker = new LifecycleMarker(entry);
            var previousCurrent = _currentLifecycleMarker.Value;
            _currentLifecycleMarker.Value = marker;
            try
            {
                aware.OnNavigatedFrom();
            }
            catch (Exception ex)
            {
                CollectErrors(errors, ex);
            }
            finally
            {
                marker.Active = false;
                _currentLifecycleMarker.Value = previousCurrent;
                entry.ExitInFlightLifecycle(gateTask);
            }
        }
        else
        {
            // No lifecycle callback: release the in-flight registration made
            // by the caller so the entry's gate is not held open.
            entry.ExitInFlightLifecycle(gateTask);
        }
    }

    /// <summary>
    /// Runs the entry's <c>OnNavigatedTo</c> callback (the async variant when
    /// implemented, otherwise the synchronous one) and collects any exception into
    /// <paramref name="errors"/> instead of propagating it. The caller registered
    /// the callback as in-flight when the hop was committed (under the navigation
    /// lock) and passes that registration as <paramref name="gateTask"/>; it is
    /// exited here when the callback finishes. Called by the region's pump while
    /// holding no locks.
    /// </summary>
    private async Task RunToCallbacksAsync(NavigationEntry entry, object? parameter, Task gateTask, List<Exception> errors)
    {
        var viewModel = entry.ViewModel;
        if (viewModel is INavigationAwareAsync asyncAware)
        {
            var token = entry.LifecycleCts.Token;
            var marker = new LifecycleMarker(entry);
            var previousCurrent = _currentLifecycleMarker.Value;
            _currentLifecycleMarker.Value = marker;
            try
            {
                await asyncAware.OnNavigatedToAsync(parameter, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (token.IsCancellationRequested)
            {
                // Expected: the page left the active position while its init was
                // still running. Not an error — the navigation itself succeeded.
                // But a cancellation wrapper around a real error is not a
                // cancellation outcome: collect the wrapped error, and swallow
                // only a pure cancellation (bare OCE or an OCE-only chain).
                // The wrapped error may itself be an aggregate, so it goes
                // through the real-leaf collector rather than a single
                // append — but without the fallback: if the wrapper carries
                // no real error at all, the outcome stays swallowed.
                if (FirstNonCancellationError(ex) is { } real)
                {
                    CollectRealErrors(errors, real);
                }
            }
            catch (Exception ex)
            {
                CollectErrors(errors, ex);
            }
            finally
            {
                marker.Active = false;
                _currentLifecycleMarker.Value = previousCurrent;
                entry.ExitInFlightLifecycle(gateTask);
            }
        }
        else if (viewModel is INavigationAware aware)
        {
            var marker = new LifecycleMarker(entry);
            var previousCurrent = _currentLifecycleMarker.Value;
            _currentLifecycleMarker.Value = marker;
            try
            {
                aware.OnNavigatedTo(parameter);
            }
            catch (Exception ex)
            {
                CollectErrors(errors, ex);
            }
            finally
            {
                marker.Active = false;
                _currentLifecycleMarker.Value = previousCurrent;
                entry.ExitInFlightLifecycle(gateTask);
            }
        }
        else
        {
            // No lifecycle callback: release the in-flight registration made at
            // commit time so the entry's gate is not held open.
            entry.ExitInFlightLifecycle(gateTask);
        }
    }

    /// <summary>
    /// Queues the disposal side of an entry's deactivation: detach the navigation
    /// reference, compute the deactivation plan and queue the page-scope disposal
    /// (plus any cache eviction the plan carries). The entry's
    /// <c>OnNavigatedFrom</c> callback is NOT run here — under the window model it
    /// already ran during the leaving phase, before the final intent was known.
    /// Must be called while holding <see cref="_navigationLock"/> but not
    /// <see cref="_stateLock"/> (the plan builder acquires the state lock itself).
    /// </summary>
    /// <param name="entry">The stack entry being deactivated.</param>
    /// <param name="destroy">True when the entry's page scope should be disposed
    /// (subject to the plan's cache write for a popped KeepAlive entry).</param>
    /// <param name="work">The hop work list to append the disposal to.</param>
    private void QueueOutgoingDisposal(NavigationEntry entry, bool destroy, TransitionWork work)
    {
        DetachNavigation(entry.ViewModel, work);

        var plan = BuildDeactivatePlan(entry, destroy);

        if (plan.ShouldDispose)
        {
            QueueScopeDisposal(entry, plan.RegionsToNotify, work);
        }

        if (plan.EvictedDisposals is not null)
        {
            foreach (var (evictedEntry, evictedRegions) in plan.EvictedDisposals)
            {
                QueueScopeDisposal(evictedEntry, evictedRegions, work);
            }
        }
    }

    /// <summary>
    /// Salvages the disposal bookkeeping of an entry whose deactivation
    /// queueing threw — <see cref="QueueOutgoingDisposal"/> starts with the
    /// entry's detach, and a custom ViewModel's detach setter is user code
    /// that can throw. Without a plan or a pending record, a popped entry's
    /// page scope would be orphaned and never disposed. The salvage mirrors
    /// <see cref="BuildDeactivatePlan"/>'s outcomes for the same inputs:
    /// a still-referenced scope records the destroy intent as pending (only
    /// when <paramref name="destroy"/> is set — a merely covered entry, or
    /// a KeepAlive pop of a shared scope, records nothing and stays alive);
    /// an unreferenced scope is queued for gated disposal directly, with
    /// the regions of any pre-existing pending record unioned into the
    /// notification set. The one deliberate divergence: where the plan
    /// would move an unreferenced KeepAlive entry into the cache, the
    /// salvage disposes its scope instead — a page whose detach throws is
    /// not kept alive.
    /// </summary>
    private void SalvageOutgoingDisposal(NavigationEntry entry, bool destroy, TransitionWork work)
    {
        string[] regionsToNotify;
        bool dispose;
        using (_stateLock.EnterScope())
        {
            var regions = new HashSet<string> { entry.RegionName };
            _pendingScopeDisposals.TryGetValue(entry.Scope, out var pending);
            if (pending is not null)
            {
                regions.UnionWith(pending.Regions);
            }

            if (IsScopeReferencedLocked(entry.Scope))
            {
                dispose = false;
                if (destroy)
                {
                    if (pending is not null)
                    {
                        pending.Regions.UnionWith(regions);
                    }
                    else
                    {
                        _pendingScopeDisposals[entry.Scope] =
                            new PendingDisposal(entry.ViewModel, regions);
                    }
                }
            }
            else
            {
                dispose = true;
            }

            regionsToNotify = [.. regions];
        }

        if (dispose)
        {
            QueueScopeDisposal(entry, regionsToNotify, work);
        }
    }

    /// <summary>
    /// Reports a disposal-gate timeout for an entry whose async lifecycle
    /// ignored its cancellation token. The navigation proceeds anyway; the
    /// entry's gate is marked abandoned so it is reported exactly once.
    /// </summary>
    private void ReportGateTimeout(NavigationEntry entry)
    {
        ReportSubscriberError(new TimeoutException(
            $"Disposal gate timed out after {DisposalGateTimeout.TotalSeconds:F0}s waiting for " +
            $"'{entry.ViewModel.GetType().FullName}' async lifecycle to complete. " +
            "The page scope is being released anyway; the ViewModel ignored its cancellation token."));
    }

    /// <summary>
    /// Queues the gated disposal of one entry's page scope: the disposal waits
    /// for the entry's in-flight lifecycle callbacks (with a timeout), and a
    /// disposal running inside the entry's own callback is deferred until the
    /// callback exits.
    /// </summary>
    private void QueueScopeDisposal(NavigationEntry entry, string[] regionsToNotify, TransitionWork work)
    {
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
            bool isSelfDispose = _currentLifecycleMarker.Value is { Active: true } selfMarker
                && ReferenceEquals(selfMarker.Entry, entry);
            var gateTask = entry.InFlightGateTask;
            // A gate the pump already timed out on (and reported) is not waited
            // on again: the scope is released exactly as after a timeout here.
            bool gateAbandoned = gateTask is not null && ReferenceEquals(entry.AbandonedGateTask, gateTask);
            if (gateTask is not null && !gateTask.IsCompleted && !isSelfDispose && !gateAbandoned)
            {
                using var delayCts = new CancellationTokenSource();
                var completed = await Task.WhenAny(gateTask, Task.Delay(DisposalGateTimeout, delayCts.Token)).ConfigureAwait(false);
                if (ReferenceEquals(completed, gateTask))
                {
                    delayCts.Cancel();
                }
                else
                {
                    entry.AbandonedGateTask = gateTask;
                    ReportGateTimeout(entry);
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
                    TrackDeferredDisposal(gate, entry.Scope, entry.ViewModel, entry.RegionName,
                        ReportSubscriberError);
                    // The deferred task raises ViewModelReleased after disposal;
                    // do not enqueue it here (would double-fire).
                }
                else
                {
                    // No callback in flight (shouldn't happen for self-dispose,
                    // but be safe): dispose inline.
                    if (await DisposeScopeAsync(entry.Scope, work.Errors).ConfigureAwait(false))
                    {
                        foreach (var region in regionsToNotify)
                        {
                            var capturedRegion = region;
                            work.Events.Add(() => RaiseViewModelReleased(capturedRegion, entry.ViewModel));
                        }
                    }
                }
                // Ownership transfers atomically to the deferred task: TrackDeferredDisposal
                // removes the scope from _pendingScopeDisposals, so a concurrent Dispose/
                // DisposeAsync cannot collect and double-dispose it.
            }
            else
            {
                if (await DisposeScopeAsync(entry.Scope, work.Errors).ConfigureAwait(false))
                {
                    foreach (var region in regionsToNotify)
                    {
                        var capturedRegion = region;
                        work.Events.Add(() => RaiseViewModelReleased(capturedRegion, entry.ViewModel));
                    }
                }

                using (_stateLock.EnterScope())
                    _pendingScopeDisposals.Remove(entry.Scope);
            }

            // The lifecycle CTS is no longer needed once the entry is gone.
            // DisposeLifecycleSources disposes it (with any settled retired
            // sources) only when no lifecycle callback is in flight — the
            // self-dispose case, where the running callback still holds the
            // token, is skipped by that check; the CTS then becomes eligible
            // for GC once the callback completes (a CTS with no registered
            // callbacks holds no unmanaged resources).
            entry.DisposeLifecycleSources();

            // Note: ViewModelReleased is enqueued above per-branch (not here),
            // to avoid double-firing when the deferred task also raises it.
        });
    }

    /// <summary>
    /// Builds the deactivation plan for an entry that is leaving the active slot:
    /// whether its page scope should be disposed and which regions to notify.
    /// When the move into the cache evicts a previous entry under the same cache
    /// key and that entry's page scope is no longer referenced anywhere else,
    /// the plan also carries that evicted entry so the caller can dispose its
    /// page scope through the standard disposal path. If the scope is still
    /// referenced, the disposal is recorded as pending instead.
    /// Mutual exclusion: a KeepAlive entry popped from the stack is moved to the
    /// cache here; an entry still on a stack (covered, not popped) is left alone.
    /// Acquires <see cref="_stateLock"/> internally.
    /// </summary>
    private DeactivatePlan BuildDeactivatePlan(NavigationEntry entry, bool destroy)
    {
        var regionsToNotify = new HashSet<string> { entry.RegionName };
        bool shouldDispose;
        List<(NavigationEntry Entry, string[] Regions)>? evictedDisposals = null;

        using (_stateLock.EnterScope())
        {
            if (_pendingScopeDisposals.TryGetValue(entry.Scope, out var pending))
            {
                regionsToNotify.UnionWith(pending.Regions);
            }

            if (IsScopeReferencedLocked(entry.Scope))
            {
                // Still needed by another stack entry or the cache: defer disposal.
                // (With mutual exclusion, "in cache" here means another entry object
                // shares this scope — e.g. a Singleton ViewModel — not this entry.)
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
            else if (!destroy && !_pendingScopeDisposals.ContainsKey(entry.Scope))
            {
                // Popped from the stack and KeepAlive: move to the cache.
                // Mutual exclusion: the entry lives in exactly one place.
                // Use the static cache key captured at push time so reads and
                // writes agree even when DI resolves a derived type.
                var cacheKey = (entry.RegionName, entry.CacheKeyType);
                if (_keepAliveCache.TryGetValue(cacheKey, out var evictedEntry)
                    && !ReferenceEquals(evictedEntry, entry))
                {
                    // The cache holds at most one entry per key, so this write
                    // evicts the previous entry. Its scope must go through the
                    // standard disposal path instead of being orphaned.
                    _keepAliveCache.Remove(cacheKey);
                    var evictedRegions = new HashSet<string> { evictedEntry.RegionName };
                    if (_pendingScopeDisposals.TryGetValue(evictedEntry.Scope, out var evictedPending))
                    {
                        evictedRegions.UnionWith(evictedPending.Regions);
                    }

                    if (IsScopeReferencedLocked(evictedEntry.Scope))
                    {
                        // Another entry still shares the scope: remember the
                        // destroy intent so the last reference disposes it.
                        if (_pendingScopeDisposals.TryGetValue(evictedEntry.Scope, out var existingPending))
                        {
                            existingPending.Regions.UnionWith(evictedRegions);
                        }
                        else
                        {
                            _pendingScopeDisposals[evictedEntry.Scope] =
                                new PendingDisposal(evictedEntry.ViewModel, evictedRegions);
                        }
                    }
                    else
                    {
                        evictedDisposals = [(evictedEntry, [.. evictedRegions])];
                    }
                }

                _keepAliveCache[cacheKey] = entry;
                shouldDispose = false;
            }
            else
            {
                shouldDispose = true;
            }
        }

        return new DeactivatePlan(shouldDispose, [.. regionsToNotify], evictedDisposals);
    }

    /// <summary>
    /// Clears the navigation service reference of a ViewModel that is no longer active.
    /// </summary>
    private static void DetachNavigation(INavigationViewModel viewModel, TransitionWork work)
    {
        // Set silently; PropertyChanged handlers are user code and must not run
        // while the silent set is applied by the pump's commit section. The
        // notification is collected and raised after the hop's callbacks.
        viewModel.SetNavigationSilent(null);
        work.NavigationChangedNotifications.Add(viewModel);
    }

    /// <summary>
    /// Attaches this navigation service to the newly activated ViewModel.
    /// </summary>
    private void AttachNavigation(INavigationViewModel viewModel, TransitionWork work)
    {
        viewModel.SetNavigationSilent(this);
        work.NavigationChangedNotifications.Add(viewModel);
    }

    // ────────────────────── Nested types ──────────────────────

    /// <summary>Work items collected under the navigation lock during one hop's
    /// commit and executed by the pump outside of it.</summary>
    private sealed class TransitionWork
    {
        // Func<Task> is used for dispose actions to avoid subtle ValueTask conversion issues.
        public List<Func<Task>> DisposeActions { get; } = [];

        public List<Action> Events { get; } = [];

        public List<Exception> Errors { get; } = [];

        /// <summary>
        /// ViewModels whose Navigation property changed under the lock.
        /// PropertyChanged handlers are user code and run after the hop's
        /// lifecycle callbacks.
        /// </summary>
        public List<INavigationViewModel> NavigationChangedNotifications { get; } = [];
    }

    /// <summary>
    /// Deactivation plan for one entry: whether to dispose its page scope and
    /// which regions to notify. <c>EvictedDisposals</c> lists cache entries
    /// evicted by this entry's move into the cache, with the regions to notify
    /// when their page scopes are disposed.
    /// </summary>
    private readonly record struct DeactivatePlan(
        bool ShouldDispose,
        string[] RegionsToNotify,
        List<(NavigationEntry Entry, string[] Regions)>? EvictedDisposals = null);

    /// <summary>One page on a region's navigation stack. The scope owns the ViewModel's lifetime.</summary>
    private sealed class NavigationEntry
    {
        private readonly object _inFlightLock = new();
        private int _inFlightCount;
        private TaskCompletionSource? _inFlightGate;
        private List<CancellationTokenSource> _retiredCts = new();

        // Retired sources whose cancellation has settled (their Cancel()
        // call has returned). A retired source is disposed only once it is
        // in this set: IsCancellationRequested turns true the moment Cancel()
        // begins, while user callbacks are still executing inside it, and
        // disposing the source underneath them would surface
        // ObjectDisposedException in the callbacks.
        private readonly HashSet<CancellationTokenSource> _settledRetiredCts = new();

        public NavigationEntry(string regionName, INavigationViewModel viewModel, object? parameter, NavigationRetention retention, IServiceScope scope, Type? cacheKeyType = null)
        {
            RegionName = regionName;
            ViewModel = viewModel;
            Parameter = parameter;
            Retention = retention;
            Scope = scope;
            // The static ViewModel type used as the KeepAlive cache key.
            // Defaults to the runtime type; callers that know the static
            // TViewModel (e.g. NavigateToAsync) pass it explicitly so cache
            // reads and writes use a consistent key even when DI resolves a
            // derived type.
            CacheKeyType = cacheKeyType ?? viewModel.GetType();
        }

        public string RegionName { get; }
        public INavigationViewModel ViewModel { get; }

        /// <summary>
        /// The service instance that created this entry. The lifecycle-entry
        /// marker is a single static AsyncLocal shared by every service
        /// instance in the process; ownership is how a service tells whether
        /// a marked entry is one of its own (in-callback classification and
        /// the lifecycle-callback guards) or merely another instance's
        /// callback running on the same flow.
        /// </summary>
        public NavigationService? OwnerService { get; set; }
        public object? Parameter { get; set; }
        public NavigationRetention Retention { get; }
        public IServiceScope Scope { get; }

        /// <summary>
        /// The in-flight gate a gate waiter already timed out on (and reported
        /// via SubscriberError). Later waiters on the same gate do not wait or
        /// report again — the scope is released as after any gate timeout.
        /// </summary>
        public volatile Task? AbandonedGateTask;

        /// <summary>
        /// The static ViewModel type this entry was navigated to: used as
        /// the KeepAlive cache key, and as the type the self-target checks
        /// compare an intent's target against.
        /// </summary>
        public Type CacheKeyType { get; }

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
        /// <see cref="CancelRetiredOutsideLocks"/>.
        /// In-flight callbacks from the previous cycle keep running under their own
        /// token and remain tracked. Retired sources are disposed when the
        /// in-flight count drops to zero, or on a later activation (when the
        /// count is zero) once their cancellation has settled.
        /// </summary>
        /// <returns>The retired cancellation token source, to be cancelled by the
        /// caller after releasing locks.</returns>
        public CancellationTokenSource BeginNewActivation()
        {
            List<CancellationTokenSource>? toDispose = null;
            CancellationTokenSource oldCts;
            lock (_inFlightLock)
            {
                oldCts = LifecycleCts;
                LifecycleCts = new CancellationTokenSource();

                // When no callbacks are in flight, dispose previously
                // retired sources whose cancellation has settled. Without
                // this, entries whose ViewModel has no lifecycle callbacks
                // at all would accumulate retired sources unboundedly,
                // because ExitInFlightLifecycle is never called for them.
                // Only settled sources are disposed: the owning thread
                // cancels the retired source outside locks via
                // CancelRetiredOutsideLocks, and a concurrent activation
                // must not dispose a source before its owner has cancelled
                // it — that would lose the cancel signal via
                // ObjectDisposedException — nor while that Cancel() is
                // still executing user callbacks. An unsettled source is
                // left in the list and picked up by a later activation.
                if (_inFlightCount == 0 && _retiredCts.Count > 0)
                {
                    for (int i = _retiredCts.Count - 1; i >= 0; i--)
                    {
                        var retiredCandidate = _retiredCts[i];
                        if (retiredCandidate.IsCancellationRequested && _settledRetiredCts.Contains(retiredCandidate))
                        {
                            toDispose ??= new List<CancellationTokenSource>();
                            toDispose.Add(retiredCandidate);
                            _retiredCts.RemoveAt(i);
                            _settledRetiredCts.Remove(retiredCandidate);
                        }
                    }
                }

                _retiredCts.Add(oldCts);
            }

            // Dispose outside the lock, matching ExitInFlightLifecycle.
            if (toDispose is not null)
            {
                foreach (var cts in toDispose)
                {
                    try { cts.Dispose(); }
                    catch { }
                }
            }

            return oldCts;
        }

        /// <summary>
        /// Cancels a source retired by <see cref="BeginNewActivation"/> outside
        /// of any navigation locks, and records its cancellation as settled
        /// once <c>Cancel()</c> has returned. Retired sources are reclaimed
        /// (disposed) only after settling: <c>IsCancellationRequested</c>
        /// alone does not show that the user callbacks <c>Cancel()</c> runs
        /// synchronously have finished executing.
        /// </summary>
        public void CancelRetiredOutsideLocks(CancellationTokenSource retired)
        {
            CancelOutsideLocks(retired);
            lock (_inFlightLock)
            {
                _settledRetiredCts.Add(retired);
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
        /// Registers a lifecycle callback as in-flight. Must be called when the
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
        /// Unregisters a lifecycle callback. When the last in-flight
        /// callback exits, retired cancellation sources that have been
        /// cancelled are disposed and any deferred scope disposals (from
        /// reentrant self-destroying navigation) are executed.
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
                    // Only reclaim sources whose cancellation has settled.
                    // A concurrent BeginNewActivation may have just retired a
                    // source whose owner has not yet cancelled it outside the
                    // locks; disposing it here would lose the cancel signal
                    // via ObjectDisposedException. A source whose Cancel() is
                    // still executing user callbacks is likewise left in the
                    // list for a later exit or activation to reclaim.
                    for (int i = _retiredCts.Count - 1; i >= 0; i--)
                    {
                        var retiredCandidate = _retiredCts[i];
                        if (retiredCandidate.IsCancellationRequested && _settledRetiredCts.Contains(retiredCandidate))
                        {
                            retiredToDispose ??= new List<CancellationTokenSource>();
                            retiredToDispose.Add(retiredCandidate);
                            _retiredCts.RemoveAt(i);
                            _settledRetiredCts.Remove(retiredCandidate);
                        }
                    }
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
        /// Disposes this entry's lifecycle cancellation sources — the current
        /// <see cref="LifecycleCts"/> and every retired source whose
        /// cancellation has settled — when the entry is destroyed. Called by
        /// the transition, cache-clear and service-disposal paths after the
        /// entry's scope disposal. When lifecycle callbacks are still in
        /// flight nothing is disposed: a running callback may still use its
        /// token, so the sources are left to become eligible for GC with the
        /// entry (the same outcome as the transition path's self-dispose
        /// case). Retired sources whose cancellation has not settled are
        /// likewise left: their owner's <c>Cancel()</c> may still be
        /// executing user callbacks, and disposing the source underneath it
        /// would surface <see cref="ObjectDisposedException"/> there.
        /// </summary>
        public void DisposeLifecycleSources()
        {
            var toDispose = new List<CancellationTokenSource>();
            lock (_inFlightLock)
            {
                if (_inFlightCount > 0)
                {
                    return;
                }

                toDispose.Add(LifecycleCts);
                for (int i = _retiredCts.Count - 1; i >= 0; i--)
                {
                    var retiredCandidate = _retiredCts[i];
                    if (retiredCandidate.IsCancellationRequested && _settledRetiredCts.Contains(retiredCandidate))
                    {
                        toDispose.Add(retiredCandidate);
                        _retiredCts.RemoveAt(i);
                        _settledRetiredCts.Remove(retiredCandidate);
                    }
                }
            }

            // Dispose outside the lock, matching ExitInFlightLifecycle.
            foreach (var cts in toDispose)
            {
                try { cts.Dispose(); }
                catch { }
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
