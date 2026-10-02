using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using ElegantSeries.Flow.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// Default <see cref="INavigationService"/> implementation (v2.0: page-level lifetime scopes).
/// </summary>
/// <remarks>
/// <para>
/// Manages one navigation stack per region, an opt-in KeepAlive page cache, and the full
/// ViewModel lifecycle (activation callbacks and disposal).
/// </para>
/// <para>
/// <b>Lifetime model (v2.0).</b> Every navigation creates one <see cref="IServiceScope"/>
/// (a "page scope") and resolves the ViewModel from it. When the page leaves the navigation
/// service's ownership, the service disposes the page scope and the DI container releases
/// the ViewModel <i>and its whole dependency graph</i>. Registration lifetimes behave as follows:
/// </para>
/// <list type="table">
/// <item><term>Transient / Scoped</term><description>A fresh instance per page; disposed with the page scope.</description></item>
/// <item><term>Singleton</term><description>Shared instance; never disposed by a page scope (it is owned by the root container).</description></item>
/// </list>
/// <para>
/// Because disposal is delegated to the container, the navigation service never calls
/// <c>Dispose</c> on a ViewModel directly. A <see cref="BaseViewModel"/> instance is rejected
/// with <see cref="InvalidOperationException"/> if a freshly resolved instance is already
/// present on any navigation stack: pushing the same instance twice would corrupt
/// back-navigation and lifecycle semantics. Navigating to the already-active instance is a
/// no-op that returns <see langword="true"/>.
/// </para>
/// <para>
/// Thread safety: every public member is safe to call from any thread. Navigation
/// transitions are serialized with an async lock, and all shared state is guarded
/// by a dedicated state lock. Lifecycle callbacks and disposal always run outside
/// both locks.
/// </para>
/// <para>
/// Disposal is best-effort <i>across</i> pages: when several page scopes are disposed together
/// (cache clear or service disposal), every scope is attempted even if one of them throws.
/// Collected exceptions are rethrown afterwards — a single exception is rethrown as-is,
/// several are wrapped in an <see cref="AggregateException"/>. <i>Within</i> one page scope,
/// the DI container stops at the first throwing disposable (platform behavior); this is
/// documented on <see cref="INavigationService.ViewModelDisposed"/> and cannot be worked
/// around by the navigation service.
/// </para>
/// </remarks>
public sealed class NavigationService(IServiceProvider serviceProvider) : INavigationService, IDisposable, IAsyncDisposable
{
    // ────────────────────────────── Fields ──────────────────────────────

    /// <summary>
    /// Creates one child scope per navigated page. Resolved once from the provided
    /// <see cref="IServiceProvider"/>, which must come from Microsoft.Extensions.DependencyInjection
    /// (or any container exposing <see cref="IServiceScopeFactory"/>).
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory =
        (serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider)))
        .GetRequiredService<IServiceScopeFactory>();

    /// <summary>Navigation stacks keyed by region name. Guarded by <see cref="_stateLock"/>.</summary>
    private readonly Dictionary<string, Stack<NavigationEntry>> _regionStacks = [];

    /// <summary>KeepAlive pages keyed by (region, ViewModel type). Guarded by <see cref="_stateLock"/>.</summary>
    private readonly Dictionary<(string Region, Type ViewModelType), NavigationEntry> _keepAliveCache = [];

    /// <summary>
    /// Page scopes removed from the cache while still referenced elsewhere.
    /// They are disposed once their last reference disappears. Guarded by <see cref="_stateLock"/>.
    /// </summary>
    private readonly Dictionary<IServiceScope, PendingDisposal> _pendingScopeDisposals = new(ReferenceEqualityComparer.Instance);

    /// <summary>Serializes navigation transitions and disposal.</summary>
    private readonly SemaphoreSlim _navigationLock = new(1, 1);

    /// <summary>
    /// Guards <see cref="_regionStacks"/>, <see cref="_keepAliveCache"/> and
    /// <see cref="_pendingScopeDisposals"/>.
    /// </summary>
    private readonly Lock _stateLock = new();

    private bool _disposed;

    // ────────────────────────────── Events ──────────────────────────────

    /// <inheritdoc />
    public event Action<string, BaseViewModel>? RegionNavigated;

    /// <inheritdoc />
    public event Action<string, BaseViewModel>? ViewModelDisposed;

    /// <inheritdoc />
    public event Action<string>? RegionCacheCleared;

    // ──────────────────────── Navigation (async) ────────────────────────

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New)
        where TViewModel : BaseViewModel
        => NavigateInternalAsync<TViewModel>(regionName, null, mode);

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New)
        where TViewModel : BaseViewModel
        => NavigateInternalAsync<TViewModel>(regionName, parameter, mode);

    /// <inheritdoc />
    public async Task<bool> GoBackAsync(string regionName = "MainRegion")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        ThrowIfDisposed();

        BaseViewModel? currentVmForGuard = null;
        using (_stateLock.EnterScope())
        {
            if (_regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 1)
            {
                currentVmForGuard = stack.Peek().ViewModel;
            }
        }

        // Run the navigation guard outside the locks to avoid deadlocks.
        if (currentVmForGuard is INavigationGuard guard)
        {
            if (!await guard.CanNavigateFromAsync().ConfigureAwait(false))
            {
                return false;
            }
        }

        var work = new TransitionWork();
        bool success = false;

        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return false;

            NavigationEntry current;
            NavigationEntry previous;

            using (_stateLock.EnterScope())
            {
                if (!_regionStacks.TryGetValue(regionName, out var stack) || stack.Count <= 1)
                {
                    return false;
                }

                current = stack.Peek();
                if (!ReferenceEquals(current.ViewModel, currentVmForGuard))
                {
                    return false;
                }

                stack.Pop();
                previous = stack.Peek();
            }

            // Queue the work items while holding _navigationLock but not _stateLock,
            // mirroring the NavigateInternalAsync path.
            QueueDeactivation(current, destroy: current.Mode != NavigationMode.KeepAlive, work);

            AttachNavigation(previous.ViewModel);
            QueueActivate(previous.ViewModel, previous.Parameter, work);
            work.Events.Add(() => RegionNavigated?.Invoke(regionName, previous.ViewModel));

            success = true;
        }
        finally
        {
            _navigationLock.Release();
        }

        if (success)
        {
            await RunTransitionAsync(work).ConfigureAwait(false);
        }

        return success;
    }

    // ───────────────────────── Queries (sync) ─────────────────────────

    /// <inheritdoc />
    public bool CanGoBack(string regionName = "MainRegion")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        using (_stateLock.EnterScope())
        {
            return _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 1;
        }
    }

    /// <inheritdoc />
    public BaseViewModel? GetCurrentViewModel(string regionName = "MainRegion")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        using (_stateLock.EnterScope())
        {
            return _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                ? stack.Peek().ViewModel
                : null;
        }
    }

    /// <inheritdoc />
    public bool IsActive<TViewModel>(string regionName = "MainRegion") where TViewModel : BaseViewModel
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        return GetCurrentViewModel(regionName) is TViewModel;
    }

    /// <inheritdoc />
    public NavigationMode? GetCurrentMode(string regionName = "MainRegion")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        using (_stateLock.EnterScope())
        {
            return _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                ? stack.Peek().Mode
                : null;
        }
    }

    // ───────────────────── Cache management (sync) ─────────────────────

    /// <inheritdoc />
    public void ClearCache(string regionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        if (_disposed) return;

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;

        // Serialized with navigation transitions: otherwise a concurrent navigation
        // could adopt a cache entry whose scope this call is about to dispose.
        _navigationLock.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            using (_stateLock.EnterScope())
            {
                targets = TakeCacheEntriesForDisposalLocked(RemoveRegionCacheEntriesLocked(regionName));
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Scope disposal runs outside the lock; the entries are already detached above.
        DisposeScopesSync(targets, pendingEvents, errors);
        pendingEvents.Add(() => RegionCacheCleared?.Invoke(regionName));

        RaiseEvents(pendingEvents);
        ThrowCollectedErrors(errors);
    }

    /// <inheritdoc />
    public void ClearAllCache()
    {
        if (_disposed) return;

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();

        // Serialized with navigation transitions (see ClearCache).
        _navigationLock.Wait();
        List<ScopeDisposalTarget> targets;
        try
        {
            if (_disposed)
            {
                return;
            }

            targets = TakeAllCacheEntriesLocked(out var affectedRegions);
            foreach (var region in affectedRegions)
            {
                var capturedRegion = region;
                pendingEvents.Add(() => RegionCacheCleared?.Invoke(capturedRegion));
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        DisposeScopesSync(targets, pendingEvents, errors);

        RaiseEvents(pendingEvents);
        ThrowCollectedErrors(errors);
    }

    // ───────────────────── Cache management (async) ─────────────────────

    /// <inheritdoc />
    public async ValueTask ClearCacheAsync(string regionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        if (_disposed) return;

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;

        // Serialized with navigation transitions: otherwise a concurrent navigation
        // could adopt a cache entry whose scope this call is about to dispose.
        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            using (_stateLock.EnterScope())
            {
                targets = TakeCacheEntriesForDisposalLocked(RemoveRegionCacheEntriesLocked(regionName));
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Scope disposal runs outside the lock; the entries are already detached above.
        await DisposeScopesAsync(targets, pendingEvents, errors).ConfigureAwait(false);
        pendingEvents.Add(() => RegionCacheCleared?.Invoke(regionName));

        RaiseEvents(pendingEvents);
        ThrowCollectedErrors(errors);
    }

    /// <inheritdoc />
    public async ValueTask ClearAllCacheAsync()
    {
        if (_disposed) return;

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();

        // Serialized with navigation transitions (see ClearCacheAsync).
        await _navigationLock.WaitAsync().ConfigureAwait(false);
        List<ScopeDisposalTarget> targets;
        try
        {
            if (_disposed)
            {
                return;
            }

            targets = TakeAllCacheEntriesLocked(out var affectedRegions);
            foreach (var region in affectedRegions)
            {
                var capturedRegion = region;
                pendingEvents.Add(() => RegionCacheCleared?.Invoke(capturedRegion));
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        await DisposeScopesAsync(targets, pendingEvents, errors).ConfigureAwait(false);

        RaiseEvents(pendingEvents);
        ThrowCollectedErrors(errors);
    }

    // ───────────────────────── Disposal ─────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Best-effort: every page scope is attempted even if one of them throws.
    /// Collected exceptions are rethrown after cleanup and events.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;

        _navigationLock.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            targets = TakeAllScopesForDisposal();
            foreach (var target in targets)
            {
                DetachNavigation(target.ViewModel);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Page-scope disposal executes arbitrary user code, so it runs outside
        // _navigationLock: concurrent callers fail fast on _disposed instead of
        // blocking on another page's disposal.
        //
        // The lock itself is intentionally not disposed: in-flight waiters release it
        // in their finally blocks, and disposing it under them would surface
        // ObjectDisposedException. The _disposed flag already makes it unusable.
        DisposeScopesSync(targets, pendingEvents, errors);
        QueueCacheClearedEvents(targets, pendingEvents);
        RaiseEvents(pendingEvents);

        ThrowCollectedErrors(errors);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Best-effort: every page scope is attempted even if one of them throws.
    /// Collected exceptions are rethrown after cleanup and events.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;

        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            targets = TakeAllScopesForDisposal();
            foreach (var target in targets)
            {
                DetachNavigation(target.ViewModel);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Page-scope disposal executes arbitrary user code (and awaits it), so it runs
        // outside _navigationLock: concurrent callers fail fast on _disposed instead of
        // blocking on another page's disposal. See Dispose() for why the lock itself
        // is not disposed.
        await DisposeScopesAsync(targets, pendingEvents, errors).ConfigureAwait(false);
        QueueCacheClearedEvents(targets, pendingEvents);
        RaiseEvents(pendingEvents);

        ThrowCollectedErrors(errors);
    }

    // ──────────────────── Navigation internals ────────────────────

    /// <summary>
    /// Core navigation logic shared by both <see cref="NavigateToAsync{TViewModel}"/> overloads.
    /// </summary>
    /// <remarks>
    /// Resolution (page-scope creation and ViewModel activation) happens outside
    /// <see cref="_navigationLock"/>; only the state transition runs under the lock and
    /// only lifecycle/disposal work items are queued there. Everything queued runs
    /// outside both locks via <see cref="RunTransitionAsync"/>.
    /// </remarks>
    private async Task<bool> NavigateInternalAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName,
        object? parameter,
        NavigationMode mode)
        where TViewModel : BaseViewModel
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        ThrowIfDisposed();

        BaseViewModel? currentVmForGuard = null;
        using (_stateLock.EnterScope())
        {
            if (_regionStacks.TryGetValue(regionName, out var peekStack) && peekStack.Count > 0)
            {
                currentVmForGuard = peekStack.Peek().ViewModel;
            }
        }

        if (currentVmForGuard is INavigationGuard guard)
        {
            if (!await guard.CanNavigateFromAsync().ConfigureAwait(false))
            {
                return false;
            }
        }

        // Resolve the target page outside _navigationLock: reuse the KeepAlive entry
        // or create a fresh page scope and resolve the ViewModel from it.
        // A concurrent ClearCache can remove or replace the cached entry between the
        // lookup and the state transition, so the entry is re-validated while holding
        // _navigationLock and the resolution is retried when it went stale. Without
        // this, navigation could push a page whose scope was already disposed.
        var cacheKey = (regionName, typeof(TViewModel));
        bool useKeepAlive = mode == NavigationMode.KeepAlive;

        IServiceScope? ownedScope = null;
        BaseViewModel targetVm = null!;
        NavigationEntry? cachedEntry = null;

        for (int attempt = 0; ; attempt++)
        {
            // After repeated races, stop consulting the cache and resolve a fresh page.
            // (Practically unreachable; guards against a pathological ClearCache loop.)
            bool consultCache = useKeepAlive && attempt < 4;
            if (consultCache)
            {
                using (_stateLock.EnterScope())
                {
                    _keepAliveCache.TryGetValue(cacheKey, out cachedEntry);
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
                catch
                {
                    // Resolution failed: the scope never produced a page; drop it quietly
                    // so the original exception propagates unmodified.
                    await DisposeScopeBestEffortAsync(ownedScope).ConfigureAwait(false);
                    throw;
                }
            }

            await _navigationLock.WaitAsync().ConfigureAwait(false);

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

        var work = new TransitionWork();
        bool success = false;
        IServiceScope? scopeToRelease = null;
        Exception? duplicateException = null;
        List<(NavigationEntry Entry, bool Destroy)> deactivations = [];
        NavigationEntry? pushedEntry = null;

        try
        {
            if (_disposed)
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

                    // Make sure the current page did not change while the guard/resolve was running.
                    var actualCurrentVm = stack.Count > 0 ? stack.Peek().ViewModel : null;
                    if (!ReferenceEquals(actualCurrentVm, currentVmForGuard))
                    {
                        // stateMismatch: abandon this navigation; the owned scope never became a page.
                        scopeToRelease = ownedScope;
                        ownedScope = null;
                    }
                    else
                    {
                        // The cached entry was re-validated before acquiring the lock,
                        // so no race adoption is needed here.
                        if (stack.Count > 0 && stack.Peek().ViewModel.GetType() == typeof(TViewModel))
                        {
                            // No-op: a page of the same type is already the active page.
                            // The freshly resolved instance is abandoned and its page scope
                            // is released below; the active instance is untouched.
                            scopeToRelease = ownedScope;
                            ownedScope = null;
                            success = true;
                        }
                        else if (ownedScope is not null && IsOnAnyStackLocked(targetVm))
                        {
                            // Fail-fast: a freshly resolved instance must not already live on a
                            // navigation stack. This is always a registration/semantics bug
                            // (typically a Singleton navigated to twice); pushing it would corrupt
                            // back-navigation and lifecycle callbacks.
                            scopeToRelease = ownedScope;
                            ownedScope = null;
                            duplicateException = new InvalidOperationException(
                                $"Cannot navigate to '{targetVm.GetType().FullName}': the same ViewModel instance " +
                                $"is already present on a navigation stack. A freshly resolved ViewModel instance cannot be pushed " +
                                $"twice; navigate back to it or use a Transient registration.");
                        }
                        else
                        {
                            var entryMode = mode == NavigationMode.ClearStack ? NavigationMode.New : mode;
                            if (cachedEntry is not null)
                            {
                                // KeepAlive reuse: share the cached entry's page scope.
                                pushedEntry = new NavigationEntry(regionName, cachedEntry.ViewModel, parameter, entryMode, cachedEntry.Scope);
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

                                if (winner is not null)
                                {
                                    scopeToRelease = ownedScope;
                                    ownedScope = null;
                                    pushedEntry = new NavigationEntry(regionName, winner.ViewModel, parameter, entryMode, winner.Scope);
                                }
                                else
                                {
                                    pushedEntry = new NavigationEntry(regionName, targetVm, parameter, entryMode, ownedScope!);
                                    ownedScope = null;
                                    if (useKeepAlive)
                                    {
                                        // No winner exists, and none can appear here: every cache
                                        // mutation requires _navigationLock, held continuously
                                        // since the validation above.
                                        _keepAliveCache[cacheKey] = pushedEntry;
                                    }
                                }
                            }

                            if (mode == NavigationMode.ClearStack)
                            {
                                while (stack.Count > 0)
                                {
                                    var old = stack.Pop();
                                    deactivations.Add((old, old.Mode != NavigationMode.KeepAlive));
                                }
                            }
                            else if (stack.Count > 0)
                            {
                                if (mode == NavigationMode.Replace)
                                {
                                    var old = stack.Pop();
                                    deactivations.Add((old, old.Mode != NavigationMode.KeepAlive));
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

            // Queue lifecycle work while holding _navigationLock but not _stateLock
            // (QueueDeactivation acquires _stateLock internally via BuildDeactivatePlan),
            // mirroring the GoBackAsync path. This avoids relying on Lock reentrancy
            // and guarantees the work list is fully built before any concurrent
            // navigation can acquire _navigationLock and queue its own work,
            // preserving lifecycle/event ordering across navigations.
            foreach (var (entry, destroy) in deactivations)
            {
                QueueDeactivation(entry, destroy, work);
            }

            if (success && pushedEntry is not null)
            {
                AttachNavigation(pushedEntry.ViewModel);
                QueueActivate(pushedEntry.ViewModel, parameter, work);
                var capturedVm = pushedEntry.ViewModel;
                work.Events.Add(() => RegionNavigated?.Invoke(regionName, capturedVm));
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // A scope resolved but never pushed is owned by nobody: release it best-effort
        // without disturbing the navigation outcome.
        if (scopeToRelease is not null)
        {
            await DisposeScopeBestEffortAsync(scopeToRelease).ConfigureAwait(false);
        }

        if (duplicateException is not null)
        {
            ExceptionDispatchInfo.Capture(duplicateException).Throw();
        }

        if (!success)
        {
            return false;
        }

        await RunTransitionAsync(work).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Determines whether the given ViewModel instance is present on any region's
    /// navigation stack. The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private bool IsOnAnyStackLocked(BaseViewModel viewModel)
    {
        foreach (var stack in _regionStacks.Values)
        {
            foreach (var entry in stack)
            {
                if (ReferenceEquals(entry.ViewModel, viewModel))
                {
                    return true;
                }
            }
        }

        return false;
    }

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
        var viewModelByScope = new Dictionary<IServiceScope, BaseViewModel>(ReferenceEqualityComparer.Instance);
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
        var viewModelByScope = new Dictionary<IServiceScope, BaseViewModel>(ReferenceEqualityComparer.Instance);

        void AddReference(IServiceScope scope, BaseViewModel viewModel, string region)
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

    // ──────────────────── Transition internals ────────────────────

    /// <summary>
    /// Executes the work items collected under the navigation lock: lifecycle callbacks,
    /// page-scope disposals and activation, then queued events. Lifecycle/disposal exceptions
    /// are collected and rethrown afterwards so one failing page never aborts the rest.
    /// </summary>
    private static async Task RunTransitionAsync(TransitionWork work)
    {
        foreach (var action in work.NavigatedFromActions)
            SafeLifecycle(action, work.Errors);

        foreach (var dispose in work.DisposeActions)
        {
            try { await dispose().ConfigureAwait(false); }
            catch (Exception ex) { work.Errors.Add(ex); }
        }

        foreach (var action in work.NavigatedToActions)
            SafeLifecycle(action, work.Errors);

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

        work.NavigatedFromActions.Add(() =>
        {
            if (entry.ViewModel is INavigationAware aware)
                aware.OnNavigatedFrom();
        });

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
    /// </summary>
    private static void QueueActivate(BaseViewModel viewModel, object? parameter, TransitionWork work)
    {
        work.NavigatedToActions.Add(() =>
        {
            if (viewModel is INavigationAware aware)
                aware.OnNavigatedTo(parameter);
        });
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
    private static void DetachNavigation(BaseViewModel viewModel)
        => viewModel.Navigation = null;

    /// <summary>
    /// Attaches this navigation service to the newly activated ViewModel.
    /// </summary>
    private void AttachNavigation(BaseViewModel viewModel)
        => viewModel.Navigation = this;

    // ────────────────────── Nested types ──────────────────────

    /// <summary>Work items collected under the navigation lock and executed outside of it.</summary>
    private sealed class TransitionWork
    {
        public List<Action> NavigatedFromActions { get; } = [];

        // Func<Task> is used for dispose actions to avoid subtle ValueTask conversion issues.
        public List<Func<Task>> DisposeActions { get; } = [];

        public List<Action> NavigatedToActions { get; } = [];

        public List<Action> Events { get; } = [];

        public List<Exception> Errors { get; } = [];
    }

    /// <summary>Deactivation plan for one entry: whether to dispose its page scope and which regions to notify.</summary>
    private readonly record struct DeactivatePlan(bool ShouldDispose, string[] RegionsToNotify);

    /// <summary>A page scope whose cache entry was removed while still referenced elsewhere.</summary>
    private sealed record PendingDisposal(BaseViewModel ViewModel, HashSet<string> Regions);

    /// <summary>A page scope selected for disposal, with the regions to notify.</summary>
    private sealed record ScopeDisposalTarget(IServiceScope Scope, BaseViewModel ViewModel, string[] Regions);

    /// <summary>One page on a region's navigation stack. The scope owns the ViewModel's lifetime.</summary>
    private sealed record NavigationEntry(string RegionName, BaseViewModel ViewModel, object? Parameter, NavigationMode Mode, IServiceScope Scope);
}
