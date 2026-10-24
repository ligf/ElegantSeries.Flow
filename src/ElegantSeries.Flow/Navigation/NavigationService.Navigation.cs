using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — hop execution for Navigate intents:
/// target resolution and stack surgery, invoked by the region's pump after the
/// departing page's window has closed, plus guard invocation and
/// stack-membership checks.
/// </summary>
public sealed partial class NavigationService
{
    /// <summary>
    /// Executes one Navigate hop: resolves the target page (reusing the
    /// KeepAlive cache entry when present) and performs the stack surgery for
    /// the intent's operation and retention. Runs after the departing page's From
    /// callbacks, with
    /// the final intent known, so the departing entry's deactivation plan is
    /// computed against the move that actually happens.
    /// </summary>
    /// <remarks>
    /// Failure before any stack surgery (the caller's token fired, resolution
    /// threw, or the resolved instance already lives on another region's
    /// stack) completes the intent's fate accordingly and revives the departing
    /// page — its From already ran, so its To runs again to restore the
    /// lifecycle pairing, and it stays the region's current page.
    /// </remarks>
    private async Task<HopResult> ExecuteNavigateHopAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        NavigationIntent intent,
        TransitionWork work,
        List<Exception> errors,
        RegionWindow window)
        where TViewModel : INavigationViewModel
    {
        var regionName = intent.RegionName;
        var parameter = intent.Parameter;
        var operation = intent.Operation;
        var retention = intent.Retention;
        var cancellationToken = intent.CancellationToken;

        // The departing entry (already run through From by the pump), if any.
        // The pump is the region's only writer, so this is still the stack top
        // when the surgery below runs.
        NavigationEntry? departing;
        using (_stateLock.EnterScope())
        {
            departing = _regionStacks.TryGetValue(regionName, out var s) && s.Count > 0 ? s.Peek() : null;
        }

        // Resolve the target page outside _navigationLock: reuse the KeepAlive entry
        // or create a fresh page scope and resolve the ViewModel from it.
        // A concurrent ClearCache can remove or replace the cached entry between the
        // lookup and the state transition, so the entry is re-validated while holding
        // _navigationLock and the resolution is retried when it went stale. Without
        // this, navigation could push a page whose scope was already disposed.
        var cacheKey = (regionName, typeof(TViewModel));
        bool useKeepAlive = retention == NavigationRetention.KeepAlive;

        IServiceScope? ownedScope = null;
        INavigationViewModel targetVm = null!;
        NavigationEntry? cachedEntry = null;

        // Recognition bookkeeping for this hop: classification invokes user
        // code (recognition votes, parameter equality), so its failures and
        // the unmatchable-parameter diagnostic are accumulated here and
        // reported at points where no state lock is held.
        List<Exception> classificationErrors = [];
        bool unmatchableRejection = false;
        NavigationEntry? recognizedCacheEntry = null;
        NavigationEntry? existingEntry = null;
        var parameterType = parameter?.GetType();

        // Reports the classification failures accumulated so far. Called
        // only at points where _navigationLock is not held: error handlers
        // are user code and may call back into the service (one calling
        // ClearCache would deadlock against the navigation lock). The
        // unmatchable-parameter diagnostic is delivered separately, from
        // the commit's finally, where the hop's outcome is known.
        void FlushRecognitionReports()
        {
            foreach (var error in classificationErrors)
            {
                ReportSubscriberError(error);
            }

            classificationErrors.Clear();
        }

        for (int attempt = 0; ; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Withdrawn before execution: release nothing (no scope exists
                // yet on the first pass; on retries the tentative scope is
                // dropped by the loop tail) and revive the departing page.
                if (ownedScope is not null)
                {
                    await DisposeScopeBestEffortAsync(ownedScope).ConfigureAwait(false);
                    ownedScope = null;
                }

                FlushRecognitionReports();
                CancelIntent(intent);
                if (departing is not null)
                {
                    await ReviveCurrentPageAsync(window, regionName, departing, intent: null, errors).ConfigureAwait(false);
                }

                return new HopResult(null, null, null);
            }

            // After repeated races, stop consulting the cache and resolve a fresh page.
            // (Practically unreachable; guards against a pathological ClearCache loop.)
            bool consultCache = useKeepAlive && attempt < 4;
            recognizedCacheEntry = null;
            if (consultCache)
            {
                using (_stateLock.EnterScope())
                {
                    _keepAliveCache.TryGetValue(cacheKey, out cachedEntry);
                }

                if (cachedEntry is not null)
                {
                    // Recognition gate (outside the lock: classification
                    // invokes user code): the cached entry is reused only
                    // when it is this navigation's target. An unrecognized
                    // entry is bypassed — left in the cache, where a later
                    // cache write of the new entry evicts it through the
                    // standard deactivation plan — and a fresh page is
                    // resolved below instead of silently re-purposing it.
                    var verdict = ClassifyCandidate(cachedEntry, parameter, classificationErrors);
                    if (verdict == RecognitionVerdict.Recognized)
                    {
                        recognizedCacheEntry = cachedEntry;
                    }
                    else
                    {
                        if (verdict == RecognitionVerdict.RejectedByUnmatchableParameter)
                        {
                            unmatchableRejection = true;
                        }

                        cachedEntry = null;
                    }
                }
            }
            else
            {
                cachedEntry = null;
            }

            if (cachedEntry is not null)
            {
                targetVm = cachedEntry.ViewModel;
            }
            else
            {
                ownedScope = _scopeFactory.CreateScope();
                try
                {
                    targetVm = ownedScope.ServiceProvider.GetRequiredService<TViewModel>();
                }
                catch (Exception ex)
                {
                    // Resolution failed: the scope never produced a page; drop it quietly.
                    await DisposeScopeBestEffortAsync(ownedScope).ConfigureAwait(false);
                    ownedScope = null;
                    FlushRecognitionReports();
                    FaultOrReport(intent, ex);
                    if (departing is not null)
                    {
                        await ReviveCurrentPageAsync(window, regionName, departing, intent: null, errors).ConfigureAwait(false);
                    }

                    return new HopResult(null, null, null);
                }
            }

            // Stack classification for this attempt, before the navigation
            // lock is taken: snapshot the region's stack, then find the entry
            // this navigation is addressed to. Classification invokes user
            // code (recognition votes, parameter equality), so it must run
            // with no locks held. The pump serializes this region's hops, so
            // the snapshot can only go stale through a concurrent disposal;
            // the commit below re-validates before it operates. (On a
            // staleness retry the classification simply runs again for the
            // new resolution; votes are documented as repeatable.)
            existingEntry = null;
            if (operation is not (NavigationOperation.Reset or NavigationOperation.Replace))
            {
                List<NavigationEntry> snapshot;
                using (_stateLock.EnterScope())
                {
                    snapshot = _regionStacks.TryGetValue(regionName, out var s) ? [.. s] : [];
                }

                existingEntry = ClassifyExistingEntry(
                    snapshot, targetVm, parameter, classificationErrors, ref unmatchableRejection);
            }

            FlushRecognitionReports();

            try
            {
                await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The lock was never acquired: the tentative resolution is orphaned.
                if (ownedScope is not null)
                {
                    await DisposeScopeBestEffortAsync(ownedScope).ConfigureAwait(false);
                    ownedScope = null;
                }

                FlushRecognitionReports();
                CancelIntent(intent);
                if (departing is not null)
                {
                    await ReviveCurrentPageAsync(window, regionName, departing, intent: null, errors).ConfigureAwait(false);
                }

                return new HopResult(null, null, null);
            }

            bool cacheValid;
            using (_stateLock.EnterScope())
            {
                cacheValid = cachedEntry is null ||
                    (_keepAliveCache.TryGetValue(cacheKey, out var current) && ReferenceEquals(current, cachedEntry));
            }

            if (cacheValid)
            {
                break; // Holding _navigationLock; proceed to the state transition.
            }

            // The cached entry went stale under us: drop the tentative resolution and retry.
            _navigationLock.Release();
            if (ownedScope is not null)
            {
                await DisposeScopeBestEffortAsync(ownedScope).ConfigureAwait(false);
                ownedScope = null;
            }
        }

        bool success = false;
        bool canceled = false;
        Exception? queueError = null;
        IServiceScope? scopeToRelease = null;
        Exception? duplicateException = null;
        List<(NavigationEntry Entry, bool Destroy)> deactivations = [];
        bool adoptedExistingEntry = false;
        NavigationEntry? pushedEntry = null;
        NavigationEntry? reactivatedEntry = null;
        Task? toGate = null;
        // Retired CTSs to cancel after the navigation lock is released.
        // Cancel() invokes user callbacks synchronously and must not run while
        // holding locks (deadlock risk).
        List<(NavigationEntry Entry, CancellationTokenSource Source)> retiredCtsList = [];

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Cancelled before the stack was touched: release the created scope.
                scopeToRelease = ownedScope;
                ownedScope = null;
                canceled = true;
            }
            else if (_disposed)
            {
                scopeToRelease = ownedScope;
                ownedScope = null;
            }
            else
            {
                using (_stateLock.EnterScope())
                {
                    if (!_regionStacks.TryGetValue(regionName, out var stack))
                    {
                        stack = new Stack<NavigationEntry>();
                        _regionStacks[regionName] = stack;
                    }

                    // The departing entry must still be the stack top: the pump
                    // is the region's only writer between the departure and here.
                    var actualCurrent = stack.Count > 0 ? stack.Peek() : null;
                    if (!ReferenceEquals(actualCurrent, departing))
                    {
                        // Only reachable when the service was disposed mid-hop
                        // (its stacks were cleared) — handled like the disposed case.
                        scopeToRelease = ownedScope;
                        ownedScope = null;
                    }
                    else
                    {
                        // The cached entry was re-validated before acquiring the lock,
                        // so no race adoption is needed here.
                        // An explicit operation takes precedence over instance reuse:
                        // Reset/Replace express stack surgery or reuse
                        // intent that must not be silently swallowed when the target type is
                        // already the active page. For the other operations the
                        // classification phase already chose the target entry;
                        // re-validate that it is still on the stack (a disposal
                        // may have cleared it while user code ran) and fall back
                        // to pushing the resolved instance when it is gone.
                        if (existingEntry is not null && !ContainsEntryLocked(stack, existingEntry))
                        {
                            existingEntry = null;
                        }

                        if (existingEntry is not null)
                        {
                            // Pop back to the existing instance (mainstream behavior):
                            // the pages above it leave the stack and the existing page
                            // is re-activated with the new parameter. A duplicate
                            // instance is never pushed. The abandoned scope (fresh
                            // singleton re-resolution) is released below.
                            scopeToRelease = ownedScope;
                            ownedScope = null;

                            while (stack.Count > 0 && !ReferenceEquals(stack.Peek(), existingEntry))
                            {
                                var old = stack.Pop();
                                deactivations.Add((old, old.Retention != NavigationRetention.KeepAlive));
                            }

                            // Reuse the entry so re-activation carries the new
                            // parameter; the ViewModel instance, page scope, mode,
                            // in-flight tracking and lifecycle state are preserved.
                            // The retired CTS is cancelled after locks are released.
                            var target = stack.Peek();
                            target.Parameter = parameter;
                            retiredCtsList.Add((target, target.BeginNewActivation()));
                            reactivatedEntry = target;

                            success = true;
                        }
                        else if (IsOnOtherRegionStackLocked(regionName, targetVm))
                        {
                            // The instance lives on a *different* region's stack: a
                            // ViewModel has a single Navigation reference and cannot
                            // be active in two regions at once.
                            scopeToRelease = ownedScope;
                            ownedScope = null;
                            duplicateException = new InvalidOperationException(
                                $"Cannot navigate to '{targetVm.GetType().FullName}': the same ViewModel instance " +
                                $"is already present on another region's navigation stack. A ViewModel instance " +
                                $"cannot be active in two regions at once.");
                        }
                        else
                        {
                            var entryRetention = retention;
                            if (cachedEntry is not null)
                            {
                                // KeepAlive reuse: share the cached entry's page scope.
                                // Mutual exclusion: the entry moves from cache to stack.
                                // Propagate the cache key so a later pop writes it back
                                // under the same key.
                                _keepAliveCache.Remove(cacheKey);
                                pushedEntry = new NavigationEntry(regionName, cachedEntry.ViewModel, parameter, entryRetention, cachedEntry.Scope, cachedEntry.CacheKeyType) { OwnerService = this };
                                adoptedExistingEntry = true;
                            }
                            else
                            {
                                // The retry loop resolves fresh without consulting the cache once
                                // attempt >= 4 (pathological ClearCache race), so a winner may
                                // have been cached concurrently. Re-check under the locks already
                                // held and adopt it: pushing a never-cached KeepAlive page would
                                // leak its scope (it pops with destroy=false, and with no cache
                                // entry nothing would ever reclaim it).
                                NavigationEntry? winner = null;
                                if (useKeepAlive)
                                {
                                    _keepAliveCache.TryGetValue(cacheKey, out winner);
                                }

                                // Adopt the winner only when it is this navigation's own
                                // resolution (a singleton resolved a second time) or the
                                // cache entry this hop already recognized. An entry that
                                // was cached concurrently and never classified — in
                                // particular the entry this hop bypassed as unrecognized —
                                // is left in the cache: pushing the fresh entry cannot leak
                                // its scope, because the entry's eventual cache write evicts
                                // the leftover through the standard deactivation plan.
                                if (winner is not null
                                    && (ReferenceEquals(winner.ViewModel, targetVm)
                                        || ReferenceEquals(winner, recognizedCacheEntry)))
                                {
                                    scopeToRelease = ownedScope;
                                    ownedScope = null;
                                    // Mutual exclusion: winner moves from cache to stack.
                                    _keepAliveCache.Remove(cacheKey);
                                    pushedEntry = new NavigationEntry(regionName, winner.ViewModel, parameter, entryRetention, winner.Scope, winner.CacheKeyType) { OwnerService = this };
                                    adoptedExistingEntry = true;
                                }
                                else
                                {
                                    pushedEntry = new NavigationEntry(regionName, targetVm, parameter, entryRetention, ownedScope!, typeof(TViewModel)) { OwnerService = this };
                                    ownedScope = null;
                                    // Mutual exclusion: a fresh KeepAlive entry lives on the
                                    // stack only; it enters the cache when popped (see
                                    // BuildDeactivatePlan). No cache write here.
                                }
                            }

                            if (operation == NavigationOperation.Reset)
                            {
                                while (stack.Count > 0)
                                {
                                    var old = stack.Pop();
                                    deactivations.Add((old, old.Retention != NavigationRetention.KeepAlive));
                                }
                            }
                            else if (stack.Count > 0)
                            {
                                if (operation == NavigationOperation.Replace)
                                {
                                    // Remove any deeper duplicate of the target instance first
                                    // (e.g. a Singleton already on this stack): it was inactive,
                                    // so no lifecycle notification, just disposal bookkeeping.
                                    // Instance match only: a different instance of the same
                                    // type deeper on the stack is a distinct page, not a duplicate.
                                    var duplicate = FindEntryOnStackLocked(stack, targetVm);
                                    if (duplicate is not null && !ReferenceEquals(stack.Peek(), duplicate))
                                    {
                                        RemoveEntryLocked(stack, duplicate, deactivations);
                                    }

                                    var old = stack.Pop();
                                    deactivations.Add((old, old.Retention != NavigationRetention.KeepAlive));
                                }
                                else
                                {
                                    deactivations.Add((stack.Peek(), false));
                                }
                            }

                            stack.Push(pushedEntry);

                            success = true;
                        }
                    }
                }
            }

            // Queue the outgoing entries' disposal work while holding
            // _navigationLock but not _stateLock (QueueOutgoingDisposal acquires
            // _stateLock internally via BuildDeactivatePlan). The departing
            // entry's From callbacks already ran during the leaving phase; only
            // detach, plan and disposal remain.
            if (!canceled)
            {
                foreach (var (entry, destroy) in deactivations)
                {
                    try
                    {
                        QueueOutgoingDisposal(entry, destroy, work);
                    }
                    catch (Exception ex)
                    {
                        // A throwing detach (user code) must not orphan the
                        // scopes of this and every later popped entry: salvage
                        // each one's disposal, finish the commit, and let the
                        // first failure fault the hop afterwards.
                        queueError ??= ex;
                        SalvageOutgoingDisposal(entry, destroy, work);
                    }
                }

                var landedEntry = pushedEntry ?? reactivatedEntry;
                if (success && landedEntry is not null)
                {
                    AttachNavigation(landedEntry.ViewModel, work);
                    // Register the activation as in-flight NOW, under the lock:
                    // a concurrent disposal starting after this point must
                    // observe it (mirrors the historical queue-time registration).
                    toGate = landedEntry.EnterInFlightLifecycle();
                }
            }
        }
        finally
        {
            _navigationLock.Release();

            // Cancel retired CTSs outside the lock: Cancel() invokes user
            // callbacks synchronously and must not run while holding locks
            // (deadlock risk). This runs in the finally — after the lock is
            // released — so a commit that throws after retiring sources
            // (e.g. an attach failure during a pop-to-existing reactivation)
            // cannot skip it: the state changes that retired them were
            // already committed under the lock, so their cancellation is
            // part of the commit. Exceptions are swallowed per source.
            foreach (var (entry, cts) in retiredCtsList)
            {
                entry.CancelRetiredOutsideLocks(cts);
            }

            // A scope resolved but never pushed is owned by nobody: release
            // it best-effort without disturbing the navigation outcome —
            // also from the finally, for the same reason.
            if (scopeToRelease is not null)
            {
                await DisposeScopeBestEffortAsync(scopeToRelease).ConfigureAwait(false);
            }

            // Deliver the unmatchable-parameter diagnostic last, with every
            // lock released (error handlers are user code; one calling
            // ClearCache would deadlock against _navigationLock above). It
            // fires only when the hop actually landed a new instance because
            // recognition failed — not for hops that were withdrawn, and
            // not when an instance was recognized after all.
            if (success && !adoptedExistingEntry && existingEntry is null && unmatchableRejection)
            {
                ReportUnmatchableParameterOnce(parameterType, typeof(TViewModel));
            }
        }

        if (queueError is not null)
        {
            // A popped entry's detach threw during the commit (its disposal
            // was salvaged per entry). The commit itself stands; fault the hop
            // so the caller observes the failure and the pump runs its
            // recovery — the same outcome as an attach throw. One asymmetry
            // with an attach throw must be repaired first: the attach runs
            // before the in-flight registration, but this throw happens after
            // it, and the pump's recovery registers its own gate — so exit
            // the commit-time registration here, or the landed entry's gate
            // count stays one too high forever and its gate task never
            // completes (every later disposal of the page would wait out
            // the full gate timeout and report a spurious timeout).
            var queueLandedEntry = pushedEntry ?? reactivatedEntry;
            if (queueLandedEntry is not null && toGate is not null)
            {
                queueLandedEntry.ExitInFlightLifecycle(toGate);
            }

            throw queueError;
        }

        if (canceled)
        {
            CancelIntent(intent);
            if (departing is not null)
            {
                await ReviveCurrentPageAsync(window, regionName, departing, intent: null, errors).ConfigureAwait(false);
            }

            return new HopResult(null, null, null);
        }

        if (duplicateException is not null)
        {
            FaultOrReport(intent, duplicateException);
            if (departing is not null)
            {
                await ReviveCurrentPageAsync(window, regionName, departing, intent: null, errors).ConfigureAwait(false);
            }

            return new HopResult(null, null, null);
        }

        if (!success)
        {
            // The service was disposed mid-hop: fire the queued PropertyChanged
            // notifications (silent sets happened under the lock) and stop.
            foreach (var vm in work.NavigationChangedNotifications)
            {
                try { RaiseNavigationChangedNotification(vm); }
                catch { }
            }

            CompleteIntent(intent, result: false);
            return new HopResult(null, null, null);
        }

        // Run the outgoing entries' page-scope disposals now that the state is
        // committed; the entering page's To callback runs afterwards, in the
        // pump's entering step.
        await RunDisposalActionsAsync(work).ConfigureAwait(false);
        errors.AddRange(work.Errors);

        if (_disposed)
        {
            RaiseEvents(work.Events, ReportSubscriberError);
            // The entering step will never run for this hop: exit the in-flight
            // registration made at commit time, otherwise the entry's gate
            // stays open and DisposeAsync waits out the full gate timeout for
            // a callback that never ran.
            var landedEntry = pushedEntry ?? reactivatedEntry;
            if (landedEntry is not null && toGate is not null)
            {
                landedEntry.ExitInFlightLifecycle(toGate);
            }

            CompleteIntent(intent, result: false);
            return new HopResult(null, null, null);
        }

        return new HopResult(pushedEntry ?? reactivatedEntry, toGate, work);
    }

    /// <summary>
    /// Invokes the ViewModel's navigation-away guard. The context-aware variant
    /// (<see cref="INavigationGuardWithContext"/>) wins when implemented; otherwise the
    /// plain <see cref="INavigationGuard"/> is used. Must be called outside the
    /// navigation locks.
    /// </summary>
    private static async Task<bool> InvokeFromGuardAsync(
        INavigationViewModel viewModel,
        NavigationGuardContext context,
        CancellationToken cancellationToken)
    {
        if (viewModel is INavigationGuardWithContext contextGuard)
        {
            return await contextGuard.CanNavigateFromAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (viewModel is INavigationGuard guard)
        {
            return await guard.CanNavigateFromAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Removes the given entry from the stack (which is not directly removable from
    /// <see cref="Stack{T}"/>). The entry was inactive, so it is queued for disposal
    /// without a lifecycle notification. The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private void RemoveEntryLocked(
        Stack<NavigationEntry> stack,
        NavigationEntry entry,
        List<(NavigationEntry Entry, bool Destroy)> deactivations)
    {
        var temp = new Stack<NavigationEntry>();
        while (stack.Count > 0)
        {
            var popped = stack.Pop();
            if (!ReferenceEquals(popped, entry))
            {
                temp.Push(popped);
            }
            else
            {
                deactivations.Add((popped, popped.Retention != NavigationRetention.KeepAlive));
            }
        }

        while (temp.Count > 0)
        {
            stack.Push(temp.Pop());
        }
    }

    /// <summary>
    /// Determines whether the given ViewModel instance is present on another region's
    /// navigation stack (excluding the specified region). The caller must hold
    /// <see cref="_stateLock"/>.
    /// </summary>
    private bool IsOnOtherRegionStackLocked(string regionName, INavigationViewModel viewModel)
    {
        foreach (var kvp in _regionStacks)
        {
            if (string.Equals(kvp.Key, regionName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var entry in kvp.Value)
            {
                if (ReferenceEquals(entry.ViewModel, viewModel))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the stack entry holding the given ViewModel instance, or
    /// <see langword="null"/> when it is not on the stack.
    /// The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private static NavigationEntry? FindEntryOnStackLocked(
        Stack<NavigationEntry> stack, INavigationViewModel viewModel)
    {
        foreach (var entry in stack)
        {
            if (ReferenceEquals(entry.ViewModel, viewModel))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the stack still holds the given entry.
    /// The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private static bool ContainsEntryLocked(Stack<NavigationEntry> stack, NavigationEntry entry)
    {
        foreach (var candidate in stack)
        {
            if (ReferenceEquals(candidate, entry))
            {
                return true;
            }
        }

        return false;
    }
}
