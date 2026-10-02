using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Core.Navigation;

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
        var keysToRemove = _keepAliveCache.Keys.Where(k => k.Region == regionName).ToList();
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
    private List<ScopeDisposalTarget> TakeAllCacheEntriesLocked(out List<string> affectedRegions)
    {
        using (_stateLock.EnterScope())
        {
            affectedRegions = _keepAliveCache.Keys.Select(k => k.Region)
                .Union(_regionStacks.Keys)
                .Distinct()
                .ToList();
            return TakeCacheEntriesForDisposalLocked(RemoveAllCacheEntriesLocked());
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
            DisposeScopeSync(target.Scope, errors);

            foreach (var region in target.Regions)
            {
                var capturedRegion = region;
                var capturedViewModel = target.ViewModel;
                pendingEvents.Add(() => ViewModelDisposed?.Invoke(capturedRegion, capturedViewModel));
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
            await DisposeScopeAsync(target.Scope, errors).ConfigureAwait(false);

            foreach (var region in target.Regions)
            {
                var capturedRegion = region;
                var capturedViewModel = target.ViewModel;
                pendingEvents.Add(() => ViewModelDisposed?.Invoke(capturedRegion, capturedViewModel));
            }
        }
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
            pendingEvents.Add(() => RegionCacheCleared?.Invoke(capturedRegion));
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
    private static async ValueTask DisposeScopeAsync(IServiceScope scope, List<Exception> errors)
    {
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
    private static void DisposeScopeSync(IServiceScope scope, List<Exception> errors)
    {
        try
        {
            scope.Dispose();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    /// <summary>
    /// Releases a page scope that was created but never became a page (lost a race or the
    /// service was disposed concurrently). Exceptions are swallowed: a redundant scope must
    /// never break navigation.
    /// </summary>
    private static async ValueTask DisposeScopeBestEffortAsync(IServiceScope scope)
    {
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
