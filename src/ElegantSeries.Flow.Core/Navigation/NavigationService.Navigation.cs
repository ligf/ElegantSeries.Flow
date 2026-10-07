using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — navigation core: <c>NavigateToAsync</c> internals,
/// guard invocation and stack-membership checks.
/// </summary>
public sealed partial class NavigationService
{
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
        NavigationMode mode,
        bool refreshIfActive,
        CancellationToken cancellationToken)
        where TViewModel : INavigationViewModel
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        INavigationViewModel? currentVmForGuard = null;
        using (_stateLock.EnterScope())
        {
            if (_regionStacks.TryGetValue(regionName, out var peekStack) && peekStack.Count > 0)
            {
                currentVmForGuard = peekStack.Peek().ViewModel;
            }
        }

        if (currentVmForGuard is not null)
        {
            var guardContext = new NavigationGuardContext(regionName, typeof(TViewModel), mode, parameter, isBack: false);
            if (!await InvokeFromGuardAsync(currentVmForGuard, guardContext).ConfigureAwait(false))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Fast path: a New-mode navigation to the already-active type needs no new
        // page scope. Resolving a throwaway ViewModel just to discard it wastes
        // construction and disposal (and can cause real side effects when the
        // constructor subscribes events or starts work), so the same-type check
        // runs here — under _navigationLock, before any scope is created.
        // The guard above already ran on this exact instance (ReferenceEquals),
        // so a refresh keeps its "guard may veto" semantics.
        if (mode == NavigationMode.New && !_disposed)
        {
            var fastWork = new TransitionWork();
            INavigationViewModel? fastVm = null;
            bool fastRefresh = false;

            await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (_stateLock.EnterScope())
                {
                    if (_regionStacks.TryGetValue(regionName, out var fastStack) &&
                        fastStack.Count > 0)
                    {
                        var peeked = fastStack.Peek();
                        if (ReferenceEquals(peeked.ViewModel, currentVmForGuard) &&
                            peeked.ViewModel.GetType() == typeof(TViewModel))
                        {
                            fastVm = peeked.ViewModel;
                            fastRefresh = refreshIfActive;
                            if (fastRefresh)
                            {
                                // Refresh: keep the active ViewModel, its stack slot,
                                // its mode and its page scope; swap the entry so the
                                // new parameter is persisted.
                                var popped = fastStack.Pop();
                                var refreshed = new NavigationEntry(
                                    popped.RegionName, popped.ViewModel, parameter, popped.Mode, popped.Scope);
                                fastStack.Push(refreshed);
                            }
                        }
                    }
                }

                // Queue lifecycle work while holding _navigationLock but not _stateLock,
                // mirroring the main path below.
                if (fastVm is not null && fastRefresh)
                {
                    // The ViewModel is already attached from its original activation.
                    QueueActivate(fastVm, parameter, fastWork);
                    var capturedVm = fastVm;
                    fastWork.Events.Add(() => RegionNavigated?.Invoke(regionName, capturedVm));
                }
            }
            finally
            {
                _navigationLock.Release();
            }

            if (fastVm is not null)
            {
                await RunTransitionAsync(fastWork).ConfigureAwait(false);
                return true;
            }
            // Not a same-type hit: fall through to the resolve loop below.
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
        INavigationViewModel targetVm = null!;
        NavigationEntry? cachedEntry = null;

        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

                // Normalize: SemaphoreSlim surfaces TaskCanceledException, but the public
                // contract is OperationCanceledException carrying the caller's token.
                throw new OperationCanceledException(cancellationToken);
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

        var work = new TransitionWork();
        bool success = false;
        bool canceled = false;
        IServiceScope? scopeToRelease = null;
        Exception? duplicateException = null;
        List<(NavigationEntry Entry, bool Destroy)> deactivations = [];
        NavigationEntry? pushedEntry = null;
        NavigationEntry? refreshEntry = null;
        NavigationEntry? reactivatedEntry = null;

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Cancelled before the stack was touched: release the created scope
                // and report cancellation below, after the scope is released.
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
                        // An explicit navigation mode takes precedence over the same-type
                        // no-op: Replace/ClearStack/KeepAlive express stack surgery or reuse
                        // intent that must not be silently swallowed when the target type is
                        // already the active page. The no-op/refresh shortcut below only
                        // applies to the default New mode.
                        if (mode == NavigationMode.New &&
                            stack.Count > 0 && stack.Peek().ViewModel.GetType() == typeof(TViewModel))
                        {
                            // No-op: a page of the same type is already the active page.
                            // The freshly resolved instance is abandoned and its page scope
                            // is released below; the active instance is untouched.
                            scopeToRelease = ownedScope;
                            ownedScope = null;
                            if (refreshIfActive)
                            {
                                // Refresh: keep the active ViewModel, its stack slot, its
                                // mode and its page scope; only the activation callbacks
                                // run again. The entry is swapped so the new parameter is
                                // persisted — a later GoBack re-activates with the refreshed
                                // parameter, not the original one (mirrors pop-to-existing).
                                var current = stack.Pop();
                                refreshEntry = new NavigationEntry(
                                    current.RegionName, current.ViewModel, parameter, current.Mode, current.Scope);
                                stack.Push(refreshEntry);
                            }

                            success = true;
                        }
                        else
                        {
                            var existingEntry = FindEntryOnStackLocked(stack, targetVm);
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
                                    deactivations.Add((old, old.Mode != NavigationMode.KeepAlive));
                                }

                                // Swap the entry so the re-activation carries the new
                                // parameter; the ViewModel instance, page scope and mode
                                // are preserved.
                                var popped = stack.Pop();
                                reactivatedEntry = new NavigationEntry(
                                    popped.RegionName, popped.ViewModel, parameter, popped.Mode, popped.Scope);
                                stack.Push(reactivatedEntry);

                                success = true;
                            }
                            else if (IsOnAnyStackLocked(targetVm))
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
            }

            // Queue lifecycle work while holding _navigationLock but not _stateLock
            // (QueueDeactivation acquires _stateLock internally via BuildDeactivatePlan),
            // mirroring the GoBackAsync path. This avoids relying on Lock reentrancy
            // and guarantees the work list is fully built before any concurrent
            // navigation can acquire _navigationLock and queue its own work,
            // preserving lifecycle/event ordering across navigations.
            if (!canceled)
            {
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
                else if (refreshEntry is not null)
                {
                    // Refresh: the entry keeps its stack slot and page scope; only the
                    // activation callbacks run again, with the new parameter.
                    // (The ViewModel is already attached from its original activation.)
                    QueueActivate(refreshEntry.ViewModel, parameter, work);
                    var capturedVm = refreshEntry.ViewModel;
                    work.Events.Add(() => RegionNavigated?.Invoke(regionName, capturedVm));
                }
                else if (reactivatedEntry is not null)
                {
                    // Pop-to-existing: the instance was detached when it was covered;
                    // re-attach it as the active page.
                    AttachNavigation(reactivatedEntry.ViewModel);
                    QueueActivate(reactivatedEntry.ViewModel, parameter, work);
                    var capturedVm = reactivatedEntry.ViewModel;
                    work.Events.Add(() => RegionNavigated?.Invoke(regionName, capturedVm));
                }
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

        // Cancellation is reported only when the stack was never touched: once the
        // stack has been updated the transition runs to completion.
        if (canceled)
        {
            throw new OperationCanceledException(cancellationToken);
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
    /// Invokes the ViewModel's navigation-away guard. The context-aware variant
    /// (<see cref="INavigationGuardWithContext"/>) wins when implemented; otherwise the
    /// plain <see cref="INavigationGuard"/> is used. Must be called outside the
    /// navigation locks.
    /// </summary>
    private static async Task<bool> InvokeFromGuardAsync(INavigationViewModel viewModel, NavigationGuardContext context)
    {
        if (viewModel is INavigationGuardWithContext contextGuard)
        {
            return await contextGuard.CanNavigateFromAsync(context).ConfigureAwait(false);
        }

        if (viewModel is INavigationGuard guard)
        {
            return await guard.CanNavigateFromAsync().ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Determines whether the given ViewModel instance is present on any region's
    /// navigation stack. The caller must hold <see cref="_stateLock"/>.
    /// </summary>
    private bool IsOnAnyStackLocked(INavigationViewModel viewModel)
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
}
