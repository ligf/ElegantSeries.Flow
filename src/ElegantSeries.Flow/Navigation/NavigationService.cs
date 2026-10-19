using System.Diagnostics.CodeAnalysis;
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
    /// Scopes that have been disposed (or collected for disposal by Dispose/DisposeAsync).
    /// Ensures exactly-once disposal when deferred tasks race with Dispose.
    /// Guarded by <see cref="_stateLock"/>.
    /// </summary>
    private readonly HashSet<IServiceScope> _disposedScopes = new(ReferenceEqualityComparer.Instance);

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
                    shouldDispose = _disposedScopes.Add(scope);
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
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => NavigateInternalAsync<TViewModel>(regionName, null, mode, refreshIfActive, cancellationToken);

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = DefaultRegionName,
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => NavigateInternalAsync<TViewModel>(regionName, parameter, mode, refreshIfActive, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> GoBackAsync(string regionName = DefaultRegionName, CancellationToken cancellationToken = default)
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
                // Hand-written loop: avoids LINQ allocation/closure while holding _stateLock.
                NavigationEntry? target = null;
                var skipped = 0;
                foreach (var entry in stack)
                {
                    if (skipped++ == 1)
                    {
                        target = entry;
                        break;
                    }
                }
                guardContext = new NavigationGuardContext(
                    regionName, target!.ViewModel.GetType(), mode: null, target.Parameter, isBack: true);
            }
        }

        // Run the navigation guard outside the locks to avoid deadlocks.
        // Race against the caller's cancellation: stop waiting promptly if cancelled.
        // The guard receives the token for cooperative cancellation.
        if (currentVmForGuard is not null && guardContext is not null)
        {
            var guardTask = InvokeFromGuardAsync(currentVmForGuard, guardContext, cancellationToken);
            var completed = await Task.WhenAny(guardTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, guardTask))
            {
                // The guard is still running; observe its eventual exception so it
                // doesn't surface as UnobservedTaskException. The navigation is
                // cancelled regardless of the guard's eventual result.
                _ = guardTask.ContinueWith(
                    t => { try { SubscriberError?.Invoke(t.Exception!.InnerException ?? t.Exception!); } catch { } },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!await guardTask.ConfigureAwait(false))
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

        CancellationTokenSource? retiredCts = null;
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
            QueueDeactivation(current, destroy: current.Mode != NavigationMode.KeepAlive, notifyLifecycle: true, work);

            // The reactivated entry gets a fresh lifecycle token: its previous CTS
            // was cancelled when it was covered. In-flight callbacks from the old
            // cycle remain tracked and are not forgotten.
            // The retired CTS is cancelled after the navigation lock is released:
            // Cancel() invokes user callbacks synchronously and must not run
            // while holding locks (deadlock risk).
            retiredCts = previous.BeginNewActivation();
            AttachNavigation(previous.ViewModel, work);
            QueueActivate(previous, previous.Parameter, work);
            QueueRegionNavigatedEvent(regionName, previous.ViewModel, work);

            success = true;
        }
        finally
        {
            _navigationLock.Release();
        }

        // Cancel outside the lock (see above).
        if (success)
        {
            NavigationEntry.CancelOutsideLocks(retiredCts);
            work.CancelQueuedCts();
            await RunTransitionAsync(work).ConfigureAwait(false);
        }
        else
        {
            // Fire queued PropertyChanged notifications: silent sets happened
            // under the lock, but RunTransitionAsync won't run.
            foreach (var vm in work.NavigationChangedNotifications)
            {
                try { vm.RaiseNavigationChanged(); }
                catch { }
            }
        }

        return success;
    }

    // ───────────────────────── Queries (sync) ─────────────────────────

    /// <inheritdoc />
    public bool CanGoBack(string regionName = DefaultRegionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        using (_stateLock.EnterScope())
        {
            return _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 1;
        }
    }

    /// <inheritdoc />
    public INavigationViewModel? GetCurrentViewModel(string regionName = DefaultRegionName)
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
    public bool IsActive<TViewModel>(string regionName = DefaultRegionName) where TViewModel : INavigationViewModel
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        return GetCurrentViewModel(regionName) is TViewModel;
    }

    /// <inheritdoc />
    public NavigationMode? GetCurrentMode(string regionName = DefaultRegionName)
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
        var ctsToCancel = new List<CancellationTokenSource>();
        List<(NavigationEntry Entry, Task Gate)> deferredSelfDisposals = [];
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
                // Self-dispose guard: if called from within a lifecycle callback whose
                // entry is being cleared, defer that entry's disposal until the callback
                // completes (via TrackDeferredDisposal below). Waiting here would
                // self-deadlock; disposing now would be use-after-dispose.
                deferredSelfDisposals = PartitionSelfEntriesLocked(removed, out var others);
                // Signal in-flight callbacks to stop. Sync ClearCache cannot wait
                // for async callbacks without risking deadlock, but it must cancel
                // their tokens before the scopes are released below.
                // CTSs are cancelled after the lock is released (see below).
                // Only entries no longer referenced by the stack: clearing the cache
                // must not cancel an active page's lifecycle token.
                CancelAndCollectGates(FilterUnreferencedEntries(others.Select(kv => kv.Value)), ctsToCancel);
                targets = TakeCacheEntriesForDisposalLocked(others);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Deferred self-disposals: TrackDeferredDisposal re-acquires _stateLock,
        // so it must run outside the critical section above.
        foreach (var (entry, gate) in deferredSelfDisposals)
        {
            TrackDeferredDisposal(gate, entry.Scope, entry.ViewModel, entry.RegionName, ex =>
            {
                try { SubscriberError?.Invoke(ex); } catch { }
            });
        }

        // Cancel outside the lock: Cancel() invokes user callbacks synchronously.
        foreach (var cts in ctsToCancel)
        {
            NavigationEntry.CancelOutsideLocks(cts);
        }

        // Scope disposal runs outside the lock; the entries are already detached above.
        DisposeScopesSync(targets, pendingEvents, errors);
        // Only report if the cache actually had entries: firing when the cache
        // was already empty would be a false positive.
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

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();

        // Serialized with navigation transitions (see ClearCache).
        WaitNavigationLock("ClearAllCache");
        List<ScopeDisposalTarget> targets;
        var ctsToCancel = new List<CancellationTokenSource>();
        List<(NavigationEntry Entry, Task Gate)> deferredSelfDisposals = [];
        try
        {
            if (_disposed)
            {
                return;
            }

            targets = TakeAllCacheEntriesLocked(out var affectedRegions, out var removedEntries, out var unreferencedEntries, out deferredSelfDisposals);
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

        // Deferred self-disposals (see ClearCache).
        foreach (var (entry, gate) in deferredSelfDisposals)
        {
            TrackDeferredDisposal(gate, entry.Scope, entry.ViewModel, entry.RegionName, ex =>
            {
                try { SubscriberError?.Invoke(ex); } catch { }
            });
        }

        // Cancel outside the lock.
        foreach (var cts in ctsToCancel)
        {
            NavigationEntry.CancelOutsideLocks(cts);
        }

        DisposeScopesSync(targets, pendingEvents, errors);

        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));
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
        List<Task> gates;
        var ctsToCancel = new List<CancellationTokenSource>();
        List<(NavigationEntry Entry, Task Gate)> deferredSelfDisposals = [];
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
                // Self-dispose guard (see ClearCache): defer the entry belonging to
                // the currently running lifecycle callback instead of waiting for it.
                deferredSelfDisposals = PartitionSelfEntriesLocked(removed, out var others);
                // Only unreferenced entries: must not cancel an active page's token.
                gates = CancelAndCollectGates(FilterUnreferencedEntries(others.Select(kv => kv.Value)), ctsToCancel);
                targets = TakeCacheEntriesForDisposalLocked(others);
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        // Deferred self-disposals (see ClearCache).
        foreach (var (entry, gate) in deferredSelfDisposals)
        {
            TrackDeferredDisposal(gate, entry.Scope, entry.ViewModel, entry.RegionName, ex =>
            {
                try { SubscriberError?.Invoke(ex); } catch { }
            });
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

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();

        // Serialized with navigation transitions (see ClearCacheAsync).
        await _navigationLock.WaitAsync().ConfigureAwait(false);
        List<ScopeDisposalTarget> targets;
        List<Task> gates;
        var ctsToCancel = new List<CancellationTokenSource>();
        List<(NavigationEntry Entry, Task Gate)> deferredSelfDisposals = [];
        try
        {
            if (_disposed)
            {
                return;
            }

            targets = TakeAllCacheEntriesLocked(out var affectedRegions, out var removedEntries, out var unreferencedEntries, out deferredSelfDisposals);
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

        // Deferred self-disposals (see ClearCache).
        foreach (var (entry, gate) in deferredSelfDisposals)
        {
            TrackDeferredDisposal(gate, entry.Scope, entry.ViewModel, entry.RegionName, ex =>
            {
                try { SubscriberError?.Invoke(ex); } catch { }
            });
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
    /// <see cref="NavigationOptions.SyncLockTimeout"/> (default 2s) for the
    /// navigation lock. If a navigation is running a slow lifecycle callback,
    /// a <see cref="TimeoutException"/> is thrown and the service is NOT
    /// disposed. Prefer <see cref="DisposeAsync"/> on UI threads.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;

        List<Action> pendingEvents = [];
        var errors = new List<Exception>();
        List<ScopeDisposalTarget> targets;
        List<CancellationTokenSource> ctsToCancel = [];

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
            ctsToCancel = CollectAllLifecycleCtsLocked();

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
        QueueCacheClearedEvents(targets, pendingEvents);
        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));

        // Note: _disposedScopes is intentionally NOT cleared here. Deferred disposal
        // tasks (from reentrant navigation) continue running in the background and
        // still check the set for exactly-once coordination. The service is being
        // shut down, so the bounded growth is acceptable.

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

        // Note: _disposedScopes is intentionally NOT cleared. A navigation that was
        // in-flight during DisposeAsync may call TrackDeferredDisposal after the
        // WaitForDeferredDisposalsAsync snapshot; its task still needs the set for
        // exactly-once coordination. The set is released with the service itself.

        QueueCacheClearedEvents(targets, pendingEvents);
        RaiseEvents(pendingEvents, ex => SubscriberError?.Invoke(ex));

        ThrowCollectedErrors(errors);
    }
}
