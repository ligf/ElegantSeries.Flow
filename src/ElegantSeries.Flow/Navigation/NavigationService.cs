using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

using static ElegantSeries.Flow.Navigation.NavigationDefaults;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Default <see cref="INavigationService"/> implementation (page-level lifetime scopes).
/// </summary>
/// <remarks>
/// <para>
/// Manages one navigation stack per region, an opt-in KeepAlive page cache, and the full
/// ViewModel lifecycle (activation callbacks and disposal).
/// </para>
/// <para>
/// <b>Lifetime model.</b> Every navigation creates one <see cref="IServiceScope"/>
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
/// Thread safety: every public member is safe to call from any thread. Within
/// each region, transitions are serialized by a per-region pump that executes
/// registered requests one hop at a time; regions proceed independently of
/// one another. All shared state is guarded by a dedicated state lock, and the
/// synchronous cache-clearing and disposal members additionally take a
/// navigation lock bounded by <see cref="NavigationOptions.SyncLockTimeout"/>.
/// Lifecycle callbacks and disposal always run outside every lock.
/// </para>
/// <para>
/// Disposal is best-effort <i>across</i> pages: when several page scopes are disposed together
/// (cache clear or service disposal), every scope is attempted even if one of them throws.
/// Collected exceptions are rethrown afterwards — a single exception is rethrown as-is,
/// several are wrapped in an <see cref="AggregateException"/>. <i>Within</i> one page scope,
/// the DI container stops at the first throwing disposable (platform behavior); this is
/// documented on <see cref="INavigationService.ViewModelReleased"/> and cannot be worked
/// around by the navigation service.
/// </para>
/// </remarks>
public sealed partial class NavigationService(IServiceProvider serviceProvider, NavigationOptions? options = null) : INavigationService, IDisposable, IAsyncDisposable
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

    private readonly NavigationOptions _options = options ?? new NavigationOptions();

    /// <summary>
    /// Page scopes removed from the cache while still referenced elsewhere.
    /// They are disposed once their last reference disappears. Guarded by <see cref="_stateLock"/>.
    /// </summary>
    private readonly Dictionary<IServiceScope, PendingDisposal> _pendingScopeDisposals = new(ReferenceEqualityComparer.Instance);

    /// <summary>Serializes navigation transitions and disposal.</summary>
    private readonly SemaphoreSlim _navigationLock = new(1, 1);

    /// <summary>
    /// Acquires <see cref="_navigationLock"/> for a synchronous API, with a timeout.
    /// Sync APIs cannot wait indefinitely: on a UI thread an unbounded wait for a
    /// lock held by a slow navigation would freeze the UI.
    /// </summary>
    /// <param name="caller">Calling method name, for the exception message.</param>
    /// <exception cref="TimeoutException">
    /// Thrown when the lock cannot be acquired within <see cref="NavigationOptions.SyncLockTimeout"/>.
    /// Prefer the async counterpart on UI threads.
    /// </exception>
    private void WaitNavigationLock(string caller)
    {
        if (!_navigationLock.Wait(_options.SyncLockTimeout))
        {
            throw new TimeoutException(
                $"{caller} timed out after {_options.SyncLockTimeout.TotalSeconds:F0}s waiting for the navigation lock. " +
                $"A navigation is likely running a slow lifecycle callback. Prefer the async counterpart " +
                $"({caller}Async) on UI threads to avoid blocking.");
        }
    }

    /// <summary>
    /// Guards <see cref="_regionStacks"/>, <see cref="_keepAliveCache"/> and
    /// <see cref="_pendingScopeDisposals"/>.
    /// </summary>
    private readonly Lock _stateLock = new();

    /// <summary>
    /// Scopes that have been claimed for disposal (or collected for disposal by
    /// Dispose/DisposeAsync). Ensures exactly-once disposal when deferred tasks race
    /// with Dispose: the first path to claim a scope disposes it, later paths skip it.
    /// Claims are made via <c>TryAdd</c> under <see cref="_stateLock"/>.
    /// </summary>
    /// <remarks>
    /// The table holds its keys weakly and never roots a scope. An entry can only
    /// disappear once the scope is unreachable everywhere — and a scope no path can
    /// reach can never be presented for disposal again — so a vanished entry cannot
    /// let a second disposal through, while a disposed page's scope (and the scoped
    /// service graph its resolved-services table still references) becomes
    /// garbage-collectable as soon as the page itself is forgotten.
    /// </remarks>
    private readonly ConditionalWeakTable<IServiceScope, object> _disposedScopes = new();

    /// <summary>Marker value stored in <see cref="_disposedScopes"/>; only key presence matters.</summary>
    private static readonly object DisposedScopeMarker = new();

    /// <summary>
    /// Tracks deferred scope disposals (from reentrant navigation or sync APIs
    /// that cannot block). Each task waits for its gate then disposes the scope.
    /// Guarded by <see cref="_deferredLock"/>. <see cref="DisposeAsync"/> waits
    /// for all tracked tasks.
    /// </summary>
    private readonly List<Task> _deferredDisposalTasks = [];
    private readonly Lock _deferredLock = new();

    /// <summary>
    /// Defers a scope disposal until the given gate task completes, then disposes
    /// the scope on the thread pool. The returned task is tracked so
    /// <see cref="DisposeAsync"/> can wait for all deferred disposals.
    /// </summary>
    private void TrackDeferredDisposal(Task gateTask, IServiceScope scope, INavigationViewModel viewModel, string regionName, Action<Exception>? errorHandler)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                // Wait for the in-flight callback with the same timeout semantics as
                // other disposal gates: an unresponsive ViewModel must not block
                // disposal forever. On timeout, report and release the scope anyway.
                using var delayCts = new CancellationTokenSource();
                var completed = await Task.WhenAny(gateTask, Task.Delay(DisposalGateTimeout, delayCts.Token)).ConfigureAwait(false);
                if (ReferenceEquals(completed, gateTask))
                {
                    // Gate won: cancel the delay to release its timer early.
                    delayCts.Cancel();
                }
                else
                {
                    try
                    {
                        errorHandler?.Invoke(new TimeoutException(
                            $"Deferred disposal gate timed out after {DisposalGateTimeout.TotalSeconds:F0}s. " +
                            "The page scope is being released anyway; the ViewModel ignored its cancellation token."));
                    }
                    catch { }
                }

                bool shouldDispose;
                using (_stateLock.EnterScope())
                {
                    // Exactly-once: skip if Dispose/DisposeAsync already took it.
                    shouldDispose = _disposedScopes.TryAdd(scope, DisposedScopeMarker);
                }

                if (shouldDispose)
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
                    finally
                    {
                        // Notify subscribers even if disposal threw, matching the
                        // non-deferred paths. Best-effort: never fail the task.
                        try
                        {
                            RaiseViewModelReleased(regionName, viewModel);
                        }
                        catch (Exception ex)
                        {
                            try { errorHandler?.Invoke(ex); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { errorHandler?.Invoke(ex); }
                catch { }
            }
        });

        // Register the task BEFORE removing from _pendingScopeDisposals: DisposeAsync
        // snapshots _deferredDisposalTasks; if the scope becomes invisible first,
        // DisposeAsync could miss both the scope and the task.
        lock (_deferredLock)
        {
            _deferredDisposalTasks.Add(task);
            // Prune completed tasks to avoid unbounded growth.
            if (_deferredDisposalTasks.Count > 32)
            {
                _deferredDisposalTasks.RemoveAll(t => t.IsCompleted);
            }
        }

        using (_stateLock.EnterScope())
        {
            // Ownership transfer: remove from _pendingScopeDisposals so TakeAllScopesForDisposal
            // cannot collect it. The deferred task is already tracked above, so DisposeAsync
            // will wait for it. If Dispose already collected it (race), the task's
            // _disposedScopes check skips the second disposal.
            _pendingScopeDisposals.Remove(scope);
        }
    }

    /// <summary>
    /// Waits for all tracked deferred disposals to complete.
    /// </summary>
    private async Task WaitForDeferredDisposalsAsync()
    {
        Task[] tasks;
        lock (_deferredLock)
        {
            tasks = [.. _deferredDisposalTasks];
        }
        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private volatile bool _disposed;

    // ────────────────────────────── Events ──────────────────────────────

    /// <inheritdoc />
    public event Action<string, INavigationViewModel>? RegionNavigated;

    /// <inheritdoc />
    public event Action<string, INavigationViewModel>? ViewModelReleased;

    /// <inheritdoc />
    public event Action<string>? RegionCacheCleared;

    /// <inheritdoc />
    public event Action<Exception>? SubscriberError;

    /// <inheritdoc />
    public IDisposable SubscribeRegionNavigated(Action<string, INavigationViewModel> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RegionNavigated += handler;
        return new EventSubscription(() => RegionNavigated -= handler);
    }

    /// <inheritdoc />
    public IDisposable SubscribeViewModelReleased(Action<string, INavigationViewModel> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ViewModelReleased += handler;
        return new EventSubscription(() => ViewModelReleased -= handler);
    }

    /// <inheritdoc />
    public IDisposable SubscribeRegionCacheCleared(Action<string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RegionCacheCleared += handler;
        return new EventSubscription(() => RegionCacheCleared -= handler);
    }

    /// <inheritdoc />
    public IDisposable SubscribeSubscriberError(Action<Exception> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SubscriberError += handler;
        return new EventSubscription(() => SubscriberError -= handler);
    }

    private sealed class EventSubscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }

    // ──────────────────────── Navigation (async) ────────────────────────

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => NavigateIntentAsync<TViewModel>(regionName, null, operation, retention, refreshIfActive, cancellationToken);

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => NavigateIntentAsync<TViewModel>(regionName, parameter, operation, retention, refreshIfActive, cancellationToken);

    /// <inheritdoc />
    public Task<bool> GoBackAsync(string regionName = DefaultRegionName, CancellationToken cancellationToken = default)
        => GoBackIntentAsync(regionName, cancellationToken);

    /// <summary>
    /// Normalizes a caller-supplied region name at the API boundary:
    /// surrounding whitespace is trimmed, and the name is then compared
    /// ordinally (case-sensitive) everywhere — region names are
    /// programmatic identifiers, following the .NET convention for
    /// identifier strings. Null, empty, and whitespace-only names are
    /// rejected. Every public entry point that accepts a region name
    /// funnels through here, so all spellings of the same trimmed name
    /// address the same region.
    /// </summary>
    private static string NormalizeRegionName(string regionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        return regionName.Trim();
    }

    // ───────────────────────── Queries (sync) ─────────────────────────

    /// <inheritdoc />
    public bool CanGoBack(string regionName = DefaultRegionName)
    {
        regionName = NormalizeRegionName(regionName);
        using (_stateLock.EnterScope())
        {
            return _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 1;
        }
    }

    /// <inheritdoc />
    public INavigationViewModel? GetCurrentViewModel(string regionName = DefaultRegionName)
    {
        regionName = NormalizeRegionName(regionName);
        using (_stateLock.EnterScope())
        {
            return _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                ? stack.Peek().ViewModel
                : null;
        }
    }

    /// <inheritdoc />
    public bool IsActive<TViewModel>(string regionName = DefaultRegionName) where TViewModel : INavigationViewModel
    {
        regionName = NormalizeRegionName(regionName);
        return GetCurrentViewModel(regionName) is TViewModel;
    }

    /// <inheritdoc />
    public NavigationRetention? GetCurrentRetention(string regionName = DefaultRegionName)
    {
        regionName = NormalizeRegionName(regionName);
        using (_stateLock.EnterScope())
        {
            return _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                ? stack.Peek().Retention
                : null;
        }
    }

    /// <inheritdoc />
    public object? GetCurrentParameter(INavigationViewModel viewModel, string regionName = DefaultRegionName)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        regionName = NormalizeRegionName(regionName);
        using (_stateLock.EnterScope())
        {
            if (!_regionStacks.TryGetValue(regionName, out var stack))
            {
                return null;
            }

            // Stack enumeration is top-first: the first match is the
            // instance's most recent activation in this region.
            foreach (var entry in stack)
            {
                if (ReferenceEquals(entry.ViewModel, viewModel))
                {
                    return entry.Parameter;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<INavigationViewModel> GetNavigationStack(string regionName = DefaultRegionName)
    {
        regionName = NormalizeRegionName(regionName);
        using (_stateLock.EnterScope())
        {
            if (!_regionStacks.TryGetValue(regionName, out var stack) || stack.Count == 0)
            {
                return [];
            }

            // Stack enumeration is top-first; the snapshot reports
            // bottom-to-top (navigation order).
            var snapshot = new INavigationViewModel[stack.Count];
            var index = stack.Count;
            foreach (var entry in stack)
            {
                snapshot[--index] = entry.ViewModel;
            }

            return snapshot;
        }
    }

    // ───────────────────── Cache management (sync) ─────────────────────

    /// <inheritdoc />
    public void ClearCache(string regionName)
    {
        regionName = NormalizeRegionName(regionName);
        if (_disposed) return;
        if (CurrentOwnLifecycleEntry is not null)
            throw new InvalidOperationException(
                "ClearCache cannot be called from within a page lifecycle callback. " +
                "Call it outside the callback instead.");

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;
        var ctsToCancel = new List<CancellationTokenSource>();
        List<NavigationEntry> cancelledEntries = [];
        bool cacheHadEntries = false;

        // Serialized with navigation transitions: otherwise a concurrent navigation
        // could adopt a cache entry whose scope this call is about to dispose.
        WaitNavigationLock("ClearCache");
        try
        {
            if (_disposed)
            {
                return;
            }

            using (_stateLock.EnterScope())
            {
                var removed = RemoveRegionCacheEntriesLocked(regionName);
                cacheHadEntries = removed.Count > 0;
                // Mutual exclusion: cache entries are never on a stack, so no
                // self-dispose guard is needed. A reentrant call from within a
                // lifecycle callback simply clears entries that do not include
                // the running page (it lives on the stack, not in the cache).
                // Signal in-flight callbacks to stop. Sync ClearCache cannot wait
                // for async callbacks without risking deadlock, but it must cancel
                // their tokens before the scopes are released below.
                // CTSs are cancelled after the lock is released (see below).
                // Only entries no longer referenced by the stack: clearing the cache
                // must not cancel an active page's lifecycle token.
                cancelledEntries = FilterUnreferencedEntries(removed.Select(kv => kv.Value));
                CancelAndCollectGates(cancelledEntries, ctsToCancel);
                targets = TakeCacheEntriesForDisposalLocked(removed);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Cancel outside the lock: Cancel() invokes user callbacks synchronously.
        foreach (var cts in ctsToCancel)
        {
            NavigationEntry.CancelOutsideLocks(cts);
        }

        // Scope disposal runs outside the lock; the entries are already detached above.
        DisposeScopesSync(targets, pendingEvents, errors);
        DisposeLifecycleSources(cancelledEntries);
        if (cacheHadEntries)
        {
            pendingEvents.Add(() => RaiseRegionCacheCleared(regionName));
        }

        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));
        ThrowCollectedErrors(errors);
    }

    /// <inheritdoc />
    public void ClearAllCache()
    {
        if (_disposed) return;
        if (CurrentOwnLifecycleEntry is not null)
            throw new InvalidOperationException(
                "ClearAllCache cannot be called from within a page lifecycle callback. " +
                "Call it outside the callback instead.");

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();

        // Serialized with navigation transitions (see ClearCache).
        WaitNavigationLock("ClearAllCache");
        List<ScopeDisposalTarget> targets;
        var ctsToCancel = new List<CancellationTokenSource>();
        List<NavigationEntry> unreferencedEntries = [];
        try
        {
            if (_disposed)
            {
                return;
            }

            targets = TakeAllCacheEntriesLocked(out var affectedRegions, out var removedEntries, out unreferencedEntries);
            // Signal in-flight callbacks to stop (sync path cannot wait).
            // Cancelled after lock release (see below).
            // Only unreferenced entries: must not cancel an active page's token.
            CancelAndCollectGates(unreferencedEntries, ctsToCancel);
            foreach (var region in affectedRegions)
            {
                var capturedRegion = region;
                pendingEvents.Add(() => RaiseRegionCacheCleared(capturedRegion));
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Cancel outside the lock.
        foreach (var cts in ctsToCancel)
        {
            NavigationEntry.CancelOutsideLocks(cts);
        }

        DisposeScopesSync(targets, pendingEvents, errors);
        DisposeLifecycleSources(unreferencedEntries);

        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));
        ThrowCollectedErrors(errors);
    }

    // ───────────────────── Cache management (async) ─────────────────────

    /// <inheritdoc />
    public async ValueTask ClearCacheAsync(string regionName)
    {
        regionName = NormalizeRegionName(regionName);
        if (_disposed) return;
        if (CurrentOwnLifecycleEntry is not null)
            throw new InvalidOperationException(
                "ClearCacheAsync cannot be called from within a page lifecycle callback. " +
                "Call it outside the callback instead.");

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;
        List<Task> gates;
        var ctsToCancel = new List<CancellationTokenSource>();
        List<NavigationEntry> cancelledEntries = [];
        bool cacheHadEntries = false;

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
                var removed = RemoveRegionCacheEntriesLocked(regionName);
                cacheHadEntries = removed.Count > 0;
                // Mutual exclusion: cache entries are never on a stack, so no
                // self-dispose guard is needed (see ClearCache).
                // Only unreferenced entries: must not cancel an active page's token.
                cancelledEntries = FilterUnreferencedEntries(removed.Select(kv => kv.Value));
                gates = CancelAndCollectGates(cancelledEntries, ctsToCancel);
                targets = TakeCacheEntriesForDisposalLocked(removed);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Cancel outside the lock: Cancel() invokes user callbacks synchronously.
        foreach (var cts in ctsToCancel)
        {
            NavigationEntry.CancelOutsideLocks(cts);
        }

        // Wait for in-flight lifecycle callbacks (with timeout) before releasing
        // their scopes. A ViewModel that ignores cancellation cannot block cache
        // clearing forever; on timeout the scope is released anyway and the hang
        // is reported via SubscriberError.
        if (gates.Count > 0)
        {
            var allGates = Task.WhenAll(gates);
            using var delayCts = new CancellationTokenSource();
            var completed = await Task.WhenAny(allGates, Task.Delay(DisposalGateTimeout, delayCts.Token)).ConfigureAwait(false);
            if (ReferenceEquals(completed, allGates))
            {
                delayCts.Cancel();
            }
            else
            {
                try
                {
                    SubscriberError?.Invoke(new TimeoutException(
                        $"ClearCacheAsync timed out after {DisposalGateTimeout.TotalSeconds:F0}s waiting for " +
                        "cached page async lifecycle to complete. The scopes are being released anyway."));
                }
                catch { }
            }
        }

        // Scope disposal runs outside the lock; the entries are already detached above.
        await DisposeScopesAsync(targets, pendingEvents, errors).ConfigureAwait(false);
        DisposeLifecycleSources(cancelledEntries);
        if (cacheHadEntries)
        {
            pendingEvents.Add(() => RaiseRegionCacheCleared(regionName));
        }

        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));
        ThrowCollectedErrors(errors);
    }

    /// <inheritdoc />
    public async ValueTask ClearAllCacheAsync()
    {
        if (_disposed) return;
        if (CurrentOwnLifecycleEntry is not null)
            throw new InvalidOperationException(
                "ClearAllCacheAsync cannot be called from within a page lifecycle callback. " +
                "Call it outside the callback instead.");

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();

        // Serialized with navigation transitions (see ClearCacheAsync).
        await _navigationLock.WaitAsync().ConfigureAwait(false);
        List<ScopeDisposalTarget> targets;
        List<Task> gates;
        var ctsToCancel = new List<CancellationTokenSource>();
        List<NavigationEntry> unreferencedEntries = [];
        try
        {
            if (_disposed)
            {
                return;
            }

            targets = TakeAllCacheEntriesLocked(out var affectedRegions, out var removedEntries, out unreferencedEntries);
            // Only unreferenced entries: must not cancel an active page's token.
            gates = CancelAndCollectGates(unreferencedEntries, ctsToCancel);
            foreach (var region in affectedRegions)
            {
                var capturedRegion = region;
                pendingEvents.Add(() => RaiseRegionCacheCleared(capturedRegion));
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Cancel outside the lock.
        foreach (var cts in ctsToCancel)
        {
            NavigationEntry.CancelOutsideLocks(cts);
        }

        // Wait for in-flight callbacks (with timeout) before releasing scopes.
        if (gates.Count > 0)
        {
            var allGates = Task.WhenAll(gates);
            using var delayCts = new CancellationTokenSource();
            var completed = await Task.WhenAny(allGates, Task.Delay(DisposalGateTimeout, delayCts.Token)).ConfigureAwait(false);
            if (ReferenceEquals(completed, allGates))
            {
                delayCts.Cancel();
            }
            else
            {
                try
                {
                    SubscriberError?.Invoke(new TimeoutException(
                        $"ClearAllCacheAsync timed out after {DisposalGateTimeout.TotalSeconds:F0}s waiting for " +
                        "cached page async lifecycle to complete. The scopes are being released anyway."));
                }
                catch { }
            }
        }

        await DisposeScopesAsync(targets, pendingEvents, errors).ConfigureAwait(false);
        DisposeLifecycleSources(unreferencedEntries);

        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));
        ThrowCollectedErrors(errors);
    }

    // ───────────────────────── Disposal ─────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Best-effort: every page scope is attempted even if one of them throws.
    /// Collected exceptions are rethrown after cleanup and events.
    /// <para>
    /// Lifecycle contract: all page lifecycle tokens are cancelled before scopes
    /// are released, but this method does NOT wait for in-flight async lifecycle
    /// callbacks to complete (blocking would risk deadlock on UI threads).
    /// Callers must ensure their <c>INavigationAwareAsync</c> callbacks observe
    /// cancellation promptly and stop touching page dependencies once cancelled.
    /// For a guaranteed wait, use <see cref="DisposeAsync"/> instead.
    /// </para>
    /// <para>
    /// Lock timeout: this method waits up to
    /// <see cref="NavigationOptions.SyncLockTimeout"/> (default 2 seconds) for the
    /// navigation lock. If a navigation is running a slow lifecycle callback, a
    /// <see cref="TimeoutException"/> is thrown and the service is NOT disposed.
    /// Prefer <see cref="DisposeAsync"/> on UI threads.
    /// </para>
    /// <para>
    /// Reentrancy: throws <see cref="InvalidOperationException"/> if called from
    /// within a page lifecycle callback. Call it outside the callback instead.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        if (CurrentOwnLifecycleEntry is not null)
            throw new InvalidOperationException(
                "Dispose cannot be called from within a page lifecycle callback. " +
                "Call it outside the callback instead.");

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;
        List<CancellationTokenSource> ctsToCancel = [];
        List<NavigationEntry> allEntries = [];

        WaitNavigationLock("Dispose");
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Collect CTSs BEFORE taking scopes: TakeAllScopesForDisposal
            // clears the stacks, after which the entries are no longer findable.
            // They are cancelled after the lock is released (see below): Cancel()
            // invokes user callbacks synchronously and must not run while holding
            // locks. Sync Dispose cannot wait for async callbacks (blocking would
            // risk deadlock on UI threads), but it must signal them to stop before
            // their scopes are released below.
            allEntries = CollectAllEntriesLocked();
            ctsToCancel = allEntries.Select(e => e.LifecycleCts).ToList();

            targets = TakeAllScopesForDisposal();
            foreach (var target in targets)
            {
                // Terminal shutdown: set silently, no PropertyChanged notification needed.
                target.ViewModel.SetNavigationSilent(null);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Pending navigation intents will never execute: complete them as false.
        // A running pump observes _disposed at its next phase boundary and stops.
        DrainWindowIntents();

        // Cancel outside the lock.
        foreach (var cts in ctsToCancel)
        {
            NavigationEntry.CancelOutsideLocks(cts);
        }

        // Page-scope disposal executes arbitrary user code, so it runs outside
        // _navigationLock: concurrent callers fail fast on _disposed instead of
        // blocking on another page's disposal.
        //
        // The lock itself is intentionally not disposed: in-flight waiters release it
        // in their finally blocks, and disposing it under them would surface
        // ObjectDisposedException. The _disposed flag already makes it unusable.
        DisposeScopesSync(targets, pendingEvents, errors);
        DisposeLifecycleSources(allEntries);
        QueueCacheClearedEvents(targets, pendingEvents);
        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));

        // Note: _disposedScopes needs no clearing here. Deferred disposal
        // tasks (from reentrant navigation) continue running in the background and
        // still claim scopes through it for exactly-once coordination; the table
        // holds scopes weakly, so leaving it in place roots nothing.

        ThrowCollectedErrors(errors);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Best-effort: every page scope is attempted even if one of them throws.
    /// Collected exceptions are rethrown after cleanup and events.
    /// <para>
    /// Reentrancy: throws <see cref="InvalidOperationException"/> if called from
    /// within a page lifecycle callback. Call it outside the callback instead.
    /// </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (CurrentOwnLifecycleEntry is not null)
            throw new InvalidOperationException(
                "DisposeAsync cannot be called from within a page lifecycle callback. " +
                "Call it outside the callback instead.");

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;
        List<NavigationEntry> entriesToCancel;

        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Collect entries to cancel AFTER the lock is released: Cancel()
            // invokes user callbacks synchronously and must not run while
            // holding locks (deadlock risk).
            entriesToCancel = CollectAllEntriesLocked();

            targets = TakeAllScopesForDisposal();
            foreach (var target in targets)
            {
                // Terminal shutdown: set silently, no PropertyChanged notification needed.
                target.ViewModel.SetNavigationSilent(null);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Pending navigation intents will never execute: complete them as false.
        // A running pump observes _disposed at its next phase boundary and stops.
        DrainWindowIntents();

        // Cancel tokens outside the lock (see above). This is a terminal navigation —
        // no page will become active again. In-flight callbacks observe the
        // cancellation and should stop touching their scopes.
        foreach (var entry in entriesToCancel)
        {
            NavigationEntry.CancelOutsideLocks(entry.LifecycleCts);
        }

        // Wait for in-flight lifecycle callbacks to finish (with timeout) before
        // releasing scopes: a callback may still touch its scope's services.
        // This mirrors the disposal gating in normal navigation transitions.
        var gateTasks = new List<Task>();
        foreach (var entry in entriesToCancel)
        {
            var gate = entry.InFlightGateTask;
            if (gate is not null && !gate.IsCompleted)
            {
                gateTasks.Add(gate);
            }
        }

        if (gateTasks.Count > 0)
        {
            var allGates = Task.WhenAll(gateTasks);
            using var delayCts = new CancellationTokenSource();
            var completed = await Task.WhenAny(allGates, Task.Delay(DisposalGateTimeout, delayCts.Token)).ConfigureAwait(false);
            if (ReferenceEquals(completed, allGates))
            {
                delayCts.Cancel();
            }
            else
            {
                try
                {
                    SubscriberError?.Invoke(new TimeoutException(
                        $"DisposeAsync timed out after {DisposalGateTimeout.TotalSeconds:F0}s waiting for " +
                        "in-flight lifecycle callbacks. Scopes are being released anyway."));
                }
                catch { }
            }
        }

        // Page-scope disposal executes arbitrary user code (and awaits it), so it runs
        // outside _navigationLock: concurrent callers fail fast on _disposed instead of
        // blocking on another page's disposal. See Dispose() for why the lock itself
        // is not disposed.
        await DisposeScopesAsync(targets, pendingEvents, errors).ConfigureAwait(false);

        // Wait for deferred disposals (e.g. from reentrant navigation): their scopes
        // were not in the targets above because the entries were already detached.
        await WaitForDeferredDisposalsAsync().ConfigureAwait(false);

        DisposeLifecycleSources(entriesToCancel);

        // Note: _disposedScopes needs no clearing. A navigation that was
        // in-flight during DisposeAsync may call TrackDeferredDisposal after the
        // WaitForDeferredDisposalsAsync snapshot; its task still claims scopes
        // through the table for exactly-once coordination. The table holds scopes
        // weakly and is released with the service itself.

        QueueCacheClearedEvents(targets, pendingEvents);
        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));

        ThrowCollectedErrors(errors);
    }
}
