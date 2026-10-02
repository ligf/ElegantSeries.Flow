using System.Diagnostics.CodeAnalysis;
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
/// <c>Dispose</c> on a ViewModel directly. A ViewModel <i>instance</i> never appears
/// twice on one region's stack: navigating to the already-active type is a no-op that
/// returns <see langword="true"/> (or a <i>refresh</i> when requested, which re-invokes
/// the active instance's activation callbacks without creating a new page scope);
/// navigating to a type whose instance lives deeper on the region's stack pops back to
/// it and re-activates it with the new parameter. Only an instance that lives on a
/// <i>different</i> region's stack is rejected with <see cref="InvalidOperationException"/>:
/// a ViewModel has a single navigation reference and cannot be active in two regions at once.
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
public sealed partial class NavigationService(IServiceProvider serviceProvider) : INavigationService, IDisposable, IAsyncDisposable
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
    public event Action<string, INavigationViewModel>? RegionNavigated;

    /// <inheritdoc />
    public event Action<string, INavigationViewModel>? ViewModelDisposed;

    /// <inheritdoc />
    public event Action<string>? RegionCacheCleared;

    // ──────────────────────── Navigation (async) ────────────────────────

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => NavigateInternalAsync<TViewModel>(regionName, null, mode, refreshIfActive, cancellationToken);

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => NavigateInternalAsync<TViewModel>(regionName, parameter, mode, refreshIfActive, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> GoBackAsync(string regionName = "MainRegion", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        INavigationViewModel? currentVmForGuard = null;
        NavigationGuardContext? guardContext = null;
        using (_stateLock.EnterScope())
        {
            if (_regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 1)
            {
                currentVmForGuard = stack.Peek().ViewModel;
                var target = stack.Skip(1).First();
                guardContext = new NavigationGuardContext(
                    regionName, target.ViewModel.GetType(), mode: null, target.Parameter, isBack: true);
            }
        }

        // Run the navigation guard outside the locks to avoid deadlocks.
        if (currentVmForGuard is not null && guardContext is not null)
        {
            if (!await InvokeFromGuardAsync(currentVmForGuard, guardContext).ConfigureAwait(false))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var work = new TransitionWork();
        bool success = false;

        try
        {
            await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normalize: SemaphoreSlim surfaces TaskCanceledException, but the public
            // contract is OperationCanceledException carrying the caller's token.
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            // No page scope is created on this path, so cancellation before the pop
            // needs no cleanup; after the pop the transition runs to completion.
            cancellationToken.ThrowIfCancellationRequested();

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
    public INavigationViewModel? GetCurrentViewModel(string regionName = "MainRegion")
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
    public bool IsActive<TViewModel>(string regionName = "MainRegion") where TViewModel : INavigationViewModel
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
}
