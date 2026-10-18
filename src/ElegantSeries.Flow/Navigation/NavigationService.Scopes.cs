using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — page-scope and KeepAlive-cache disposal.
/// </summary>
public sealed partial class NavigationService
{
    // ────────────────────── Cache internals ──────────────────────

    /// <summary>
    /// Removes every KeepAlive entry of one region from the cache and returns them.
    /// The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private List<KeyValuePair<(string Region, Type ViewModelType), NavigationEntry>> RemoveRegionCacheEntriesLocked(string regionName)
    {
        // Hand-written loop: avoids LINQ allocation/closure while holding _stateLock.
        List<(string Region, Type ViewModelType)> keysToRemove = [];
        foreach (var key in _keepAliveCache.Keys)
        {
            if (key.Region == regionName)
            {
                keysToRemove.Add(key);
            }
        }

        List<KeyValuePair<(string Region, Type ViewModelType), NavigationEntry>> removed = [];
        foreach (var key in keysToRemove)
        {
            if (_keepAliveCache.Remove(key, out var entry))
            {
                removed.Add(new(key, entry));
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes every KeepAlive entry from the cache and returns them.
    /// The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private List<KeyValuePair<(string Region, Type ViewModelType), NavigationEntry>> RemoveAllCacheEntriesLocked()
    {
        var removed = _keepAliveCache.ToList();
        _keepAliveCache.Clear();
        return removed;
    }

    /// <summary>
    /// Splits removed cache entries into page scopes that can be disposed now and ones
    /// whose disposal must be deferred until their last stack/cache reference is gone.
    /// The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private List<ScopeDisposalTarget> TakeCacheEntriesForDisposalLocked(
        IEnumerable<KeyValuePair<(string Region, Type ViewModelType), NavigationEntry>> removedEntries)
    {
        var regionsByScope = new Dictionary<IServiceScope, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var viewModelByScope = new Dictionary<IServiceScope, INavigationViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var entry in removedEntries)
        {
            var scope = entry.Value.Scope;
            if (!regionsByScope.TryGetValue(scope, out var regions))
            {
                regions = [];
                regionsByScope.Add(scope, regions);
                viewModelByScope.Add(scope, entry.Value.ViewModel);
            }

            regions.Add(entry.Key.Region);
        }

        List<ScopeDisposalTarget> readyToDispose = [];
        foreach (var (scope, regions) in regionsByScope)
        {
            if (IsScopeReferencedLocked(scope))
            {
                if (_pendingScopeDisposals.TryGetValue(scope, out var pending))
                {
                    pending.Regions.UnionWith(regions);
                }
                else
                {
                    _pendingScopeDisposals.Add(scope, new PendingDisposal(viewModelByScope[scope], new HashSet<string>(regions)));
                }

                continue;
            }

            if (_pendingScopeDisposals.Remove(scope, out var previouslyPending))
            {
                regions.UnionWith(previouslyPending.Regions);
            }

            readyToDispose.Add(new ScopeDisposalTarget(scope, viewModelByScope[scope], [.. regions]));
        }

        return readyToDispose;
    }

    /// <summary>
    /// Partitions removed cache entries into those belonging to the currently running
    /// lifecycle callback (self) versus others. Self entries are moved to
    /// <see cref="_pendingScopeDisposals"/> and their in-flight gates are returned for
    /// deferred disposal via <see cref="TrackDeferredDisposal"/>; the caller must invoke
    /// <see cref="TrackDeferredDisposal"/> outside <see cref="_stateLock"/> (it re-acquires
    /// the lock). Non-self entries are returned for the normal disposal path.
    /// Self entries from synchronous callbacks (no in-flight gate) are restored to the
    /// cache: there is no gate to defer on, and immediate disposal would be
    /// use-after-dispose.
    /// The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private List<(NavigationEntry Entry, Task Gate)> PartitionSelfEntriesLocked(
        List<KeyValuePair<(string Region, Type ViewModelType), NavigationEntry>> removed,
        out List<KeyValuePair<(string Region, Type ViewModelType), NavigationEntry>> others)
    {
        var currentEntry = _currentLifecycleEntry.Value;
        var deferred = new List<(NavigationEntry Entry, Task Gate)>();
        others = new List<KeyValuePair<(string Region, Type ViewModelType), NavigationEntry>>(removed.Count);

        foreach (var kv in removed)
        {
            var entry = kv.Value;
            // Self-dispose: the entry's scope belongs to the lifecycle callback
            // currently running on this async context. Disposing it now would be
            // use-after-dispose; waiting for its gate would self-deadlock.
            if (currentEntry is not null && ReferenceEquals(entry.Scope, currentEntry.Scope))
            {
                var gate = entry.InFlightGateTask;
                if (gate is not null && !gate.IsCompleted)
                {
                    // Async callback: defer via the pending + TrackDeferredDisposal path.
                    var regions = new HashSet<string> { kv.Key.Region };
                    if (_pendingScopeDisposals.TryGetValue(entry.Scope, out var pending))
                    {
                        pending.Regions.UnionWith(regions);
                    }
                    else
                    {
                        _pendingScopeDisposals.Add(entry.Scope, new PendingDisposal(entry.ViewModel, regions));
                    }

                    deferred.Add((entry, gate));
                    continue;
                }

                // Sync callback (no gate): restore to cache. The entry stays alive;
                // call ClearCache again after the callback if it must be dropped.
                _keepAliveCache[kv.Key] = entry;
                continue;
            }

            others.Add(kv);
        }

        return deferred;
    }

    /// <summary>
    /// Cancels the lifecycle tokens of removed cache entries and collects their
    /// in-flight gate tasks. Must be called while holding the navigation lock
    /// (before the entries are detached). The caller waits for the gates outside
    /// the lock, then disposes the scopes.
    /// </summary>
    private static List<Task> CancelAndCollectGates(IEnumerable<NavigationEntry> entries, List<CancellationTokenSource> ctsToCancel)
    {
        var gates = new List<Task>();
        foreach (var entry in entries)
        {
            // Defer Cancel() until after locks are released: it invokes user
            // callbacks synchronously which may reenter the service (deadlock).
            ctsToCancel.Add(entry.LifecycleCts);
            var gate = entry.InFlightGateTask;
            if (gate is not null && !gate.IsCompleted)
            {
                gates.Add(gate);
            }
        }
        return gates;
    }

    /// <summary>
    /// Filters cache-removed entries to only those whose scopes are no longer referenced
    /// by any navigation stack. Entries still referenced (active pages) keep their
    /// lifecycle tokens: clearing the cache must not cancel an active page.
    /// The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private List<NavigationEntry> FilterUnreferencedEntries(IEnumerable<NavigationEntry> entries)
    {
        var result = new List<NavigationEntry>();
        foreach (var entry in entries)
        {
            if (!IsScopeReferencedLocked(entry.Scope))
            {
                result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>
    /// Determines whether any region stack or the KeepAlive cache still references the
    /// given page scope. The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private bool IsScopeReferencedLocked(IServiceScope scope)
    {
        foreach (var stack in _regionStacks.Values)
        {
            foreach (var entry in stack)
            {
                if (ReferenceEquals(entry.Scope, scope))
                {
                    return true;
                }
            }
        }

        foreach (var cached in _keepAliveCache.Values)
        {
            if (ReferenceEquals(cached.Scope, scope))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes every KeepAlive entry from the cache and splits them into page scopes that
    /// can be disposed now versus ones whose disposal must be deferred.
    /// The caller must hold <see cref="_navigationLock"/> (but not <see cref="_stateLock"/>).
    /// </summary>
    private List<ScopeDisposalTarget> TakeAllCacheEntriesLocked(out List<string> affectedRegions, out List<NavigationEntry> removedEntries, out List<NavigationEntry> unreferencedEntries, out List<(NavigationEntry Entry, Task Gate)> deferredSelfDisposals)
    {
        using (_stateLock.EnterScope())
        {
            // Hand-written loops: avoids LINQ allocation/closure while holding _stateLock.
            var regions = new HashSet<string>();
            foreach (var key in _keepAliveCache.Keys)
            {
                regions.Add(key.Region);
            }
            foreach (var region in _regionStacks.Keys)
            {
                regions.Add(region);
            }
            affectedRegions = new List<string>(regions);
            var removed = RemoveAllCacheEntriesLocked();
            // Self-dispose guard: see PartitionSelfEntriesLocked.
            deferredSelfDisposals = PartitionSelfEntriesLocked(removed, out var others);
            removedEntries = new List<NavigationEntry>(others.Count);
            foreach (var kv in others)
            {
                removedEntries.Add(kv.Value);
            }
            // Only unreferenced entries get their lifecycle tokens cancelled:
            // clearing the cache must not cancel an active page.
            unreferencedEntries = FilterUnreferencedEntries(removedEntries);
            return TakeCacheEntriesForDisposalLocked(others);
        }
    }

    // ───────────────────── Disposal internals ─────────────────────

    /// <summary>
    /// Disposes removed cache entries on the synchronous path. Every page scope is
    /// attempted even if one throws; exceptions are collected into <paramref name="errors"/>.
    /// </summary>
    private void DisposeScopesSync(
        List<ScopeDisposalTarget> targets,
        List<Action> pendingEvents,
        List<Exception> errors)
    {
        foreach (var target in targets)
        {
            // Only raise ViewModelReleased if the scope was actually disposed
            // (exactly-once: a racing deferred disposal may have taken it).
            if (DisposeScopeSync(target.Scope, errors))
            {
                foreach (var region in target.Regions)
                {
                    var capturedRegion = region;
                    var capturedViewModel = target.ViewModel;
                    pendingEvents.Add(() => RaiseViewModelReleased(capturedRegion, capturedViewModel));
                }
            }
        }
    }

    /// <summary>
    /// Disposes removed cache entries on the asynchronous path. Every page scope is
    /// attempted even if one throws; exceptions are collected into <paramref name="errors"/>.
    /// </summary>
    private async ValueTask DisposeScopesAsync(
        List<ScopeDisposalTarget> targets,
        List<Action> pendingEvents,
        List<Exception> errors)
    {
        foreach (var target in targets)
        {
            // Only raise ViewModelReleased if the scope was actually disposed
            // (exactly-once: a racing deferred disposal may have taken it).
            if (await DisposeScopeAsync(target.Scope, errors).ConfigureAwait(false))
            {
                foreach (var region in target.Regions)
                {
                    var capturedRegion = region;
                    var capturedViewModel = target.ViewModel;
                    pendingEvents.Add(() => RaiseViewModelReleased(capturedRegion, capturedViewModel));
                }
            }
        }
    }

    /// <summary>
    /// Collects every entry from all region stacks and the KeepAlive cache.
    /// The caller must hold <see cref="_navigationLock"/> (and <see cref="_stateLock"/>
    /// is acquired internally for the cache).
    /// </summary>
    private List<NavigationEntry> CollectAllEntriesLocked()
    {
        var entries = new List<NavigationEntry>();
        using (_stateLock.EnterScope())
        {
            foreach (var stack in _regionStacks.Values)
            {
                entries.AddRange(stack);
            }

            entries.AddRange(_keepAliveCache.Values);
        }

        return entries;
    }

    /// <summary>
    /// Collects the lifecycle CTS of every tracked entry for cancellation outside
    /// the lock. Called by sync <see cref="NavigationService.Dispose"/> (which
    /// cannot wait for async callbacks) to signal in-flight work to stop before
    /// scopes are released. Must be called while holding the navigation lock;
    /// the returned sources must be cancelled after the lock is released via
    /// <see cref="NavigationEntry.CancelOutsideLocks"/>.
    /// </summary>
    private List<CancellationTokenSource> CollectAllLifecycleCtsLocked()
    {
        var list = new List<CancellationTokenSource>();
        foreach (var entry in CollectAllEntriesLocked())
        {
            list.Add(entry.LifecycleCts);
        }
        return list;
    }

    /// <summary>
    /// Takes every page scope owned by the service (region stacks, KeepAlive cache and
    /// deferred disposals) and clears all of them. Each distinct scope is returned once.
    /// The caller must not hold <see cref="_stateLock"/>.
    /// </summary>
    private List<ScopeDisposalTarget> TakeAllScopesForDisposal()
    {
        var regionsByScope = new Dictionary<IServiceScope, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var viewModelByScope = new Dictionary<IServiceScope, INavigationViewModel>(ReferenceEqualityComparer.Instance);

        void AddReference(IServiceScope scope, INavigationViewModel viewModel, string region)
        {
            if (!regionsByScope.TryGetValue(scope, out var regions))
            {
                regions = [];
                regionsByScope.Add(scope, regions);
                viewModelByScope.Add(scope, viewModel);
            }

            regions.Add(region);
        }

        using (_stateLock.EnterScope())
        {
            foreach (var (region, stack) in _regionStacks)
            {
                foreach (var entry in stack)
                {
                    AddReference(entry.Scope, entry.ViewModel, region);
                }
            }

            foreach (var (key, cached) in _keepAliveCache)
            {
                AddReference(cached.Scope, cached.ViewModel, key.Region);
            }

            foreach (var (scope, pending) in _pendingScopeDisposals)
            {
                // The pending scope's regions were captured when its cache entry was removed.
                foreach (var region in pending.Regions)
                {
                    AddReference(scope, pending.ViewModel, region);
                }
            }

            _regionStacks.Clear();
            _keepAliveCache.Clear();
            _pendingScopeDisposals.Clear();
        }

        return regionsByScope
            .Select(kvp => new ScopeDisposalTarget(kvp.Key, viewModelByScope[kvp.Key], [.. kvp.Value]))
            .ToList();
    }

    /// <summary>
    /// Queues a <see cref="INavigationService.RegionCacheCleared"/> notification for every
    /// region affected by a service-wide disposal.
    /// </summary>
    private void QueueCacheClearedEvents(List<ScopeDisposalTarget> targets, List<Action> pendingEvents)
    {
        foreach (var region in targets.SelectMany(t => t.Regions).Distinct())
        {
            var capturedRegion = region;
            pendingEvents.Add(() => RaiseRegionCacheCleared(capturedRegion));
        }
    }

    /// <summary>
    /// Disposes a single page scope, preferring <see cref="IAsyncDisposable"/> when the
    /// scope implementation supports it. Never throws: exceptions are collected into
    /// <paramref name="errors"/>.
    /// </summary>
    /// <remarks>
    /// Within one page scope the DI container stops disposing remaining services after the
    /// first throwing disposable (platform behavior, sync and async alike). Across page
    /// scopes the navigation service still attempts every scope.
    /// </remarks>
    private async ValueTask<bool> DisposeScopeAsync(IServiceScope scope, List<Exception> errors)
    {
        bool shouldDispose;
        using (_stateLock.EnterScope())
        {
            // Exactly-once: skip if already disposed (e.g. by a racing Dispose).
            shouldDispose = _disposedScopes.Add(scope);
        }

        if (!shouldDispose)
        {
            return false;
        }

        try
        {
            if (scope is IAsyncDisposable asyncScope)
            {
                await asyncScope.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope.Dispose();
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        return true;
    }

    /// <summary>
    /// Disposes a single page scope on the synchronous path via <see cref="IDisposable"/>.
    /// Never throws: exceptions are collected into <paramref name="errors"/>.
    /// </summary>
    /// <remarks>
    /// Platform behavior: if the page scope contains a service that only implements
    /// <see cref="IAsyncDisposable"/>, the DI container throws
    /// <see cref="InvalidOperationException"/> telling the caller to use
    /// <c>DisposeAsync</c> instead. Pages that need asynchronous cleanup must be torn
    /// down through the asynchronous APIs (<c>ClearCacheAsync</c>, <c>DisposeAsync</c>,
    /// <c>GoBackAsync</c>, <c>NavigateToAsync</c>); the synchronous APIs only support
    /// synchronously-disposable pages.
    /// </remarks>
    private bool DisposeScopeSync(IServiceScope scope, List<Exception> errors)
    {
        bool shouldDispose;
        using (_stateLock.EnterScope())
        {
            // Exactly-once: skip if already disposed (e.g. by a racing Dispose).
            shouldDispose = _disposedScopes.Add(scope);
        }

        if (!shouldDispose)
        {
            return false;
        }

        try
        {
            scope.Dispose();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        return true;
    }

    /// <summary>
    /// Releases a page scope that was created but never became a page (lost a race or the
    /// service was disposed concurrently). Exceptions are swallowed: a redundant scope must
    /// never break navigation.
    /// </summary>
    /// <remarks>
    /// Goes through the <see cref="_disposedScopes"/> exactly-once check like all other
    /// disposal paths: callers must only pass scopes that were never tracked, but the
    /// check makes a future misuse fail safe instead of double-disposing.
    /// </remarks>
    private async ValueTask DisposeScopeBestEffortAsync(IServiceScope scope)
    {
        bool shouldDispose;
        using (_stateLock.EnterScope())
        {
            shouldDispose = _disposedScopes.Add(scope);
        }

        if (!shouldDispose)
        {
            return;
        }

        try
        {
            if (scope is IAsyncDisposable asyncScope)
            {
                await asyncScope.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope.Dispose();
            }
        }
        catch
        {
            // Best-effort cleanup of a redundant scope; never propagate.
        }
    }

    /// <summary>
    /// Rethrows collected exceptions: a single exception is rethrown preserving its
    /// original stack trace; several are wrapped in an <see cref="AggregateException"/>.
    /// Does nothing when <paramref name="errors"/> is empty.
    /// </summary>
    private static void ThrowCollectedErrors(List<Exception> errors)
    {
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }
        else if (errors.Count > 1)
        {
            throw new AggregateException(
                "The navigation operation completed with lifecycle or disposal errors.",
                errors);
        }
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if the service has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    // ────────────────────── Nested types ──────────────────────

    /// <summary>A page scope whose cache entry was removed while still referenced elsewhere.</summary>
    private sealed record PendingDisposal(INavigationViewModel ViewModel, HashSet<string> Regions);

    /// <summary>A page scope selected for disposal, with the regions to notify.</summary>
    private sealed record ScopeDisposalTarget(IServiceScope Scope, INavigationViewModel ViewModel, string[] Regions);
}
