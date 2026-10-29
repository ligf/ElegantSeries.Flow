using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>
/// Disposal interleavings: randomized storms and directed races between
/// navigation work and service disposal (Dispose / DisposeAsync),
/// asserting the invariants of the disposal state machine
/// (disposal-state-machine.md, §6):
/// I1 every page scope is disposed exactly once (scope ledger balances),
/// I4 concurrent DisposeAsync calls join one operation and observe the
/// same outcome, I5 every call settles within a bounded time, and
/// I6 after disposal the navigation entry points reject and the queries
/// return empty state. The deferred-disposal path (a page disposing its
/// own scope from inside its callback) is staged deterministically by
/// the directed white-box tests in NavigationServiceTests; here the
/// callback self-dispose of the SERVICE is covered instead.
/// </summary>
public class DisposalInterleavingTests
{
    // ── Shared fixtures ───────────────────────────────────────

    /// <summary>A one-shot gate a lifecycle callback can block on:
    /// signals when the callback has been entered, completes when the
    /// test releases it. The callback deliberately ignores its
    /// cancellation token, so only the framework's gate wait (not the
    /// cancellation) can be what disposal is waiting for.</summary>
    public sealed class CallbackGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public async Task WaitAsync()
        {
            _entered.TrySetResult();
            await _release.Task;
        }

        public void Release() => _release.TrySetResult();
    }

    /// <summary>Page whose OnNavigatedToAsync blocks on the shared gate,
    /// ignoring cancellation until released.</summary>
    public sealed class GatedPage : NavigationViewModelBase, INavigationAwareAsync
    {
        private readonly CallbackGate _gate;

        public GatedPage(PageScopeMarker marker, CallbackGate gate)
        {
            _ = marker;
            _gate = gate;
        }

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default) =>
            await _gate.WaitAsync();

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Hook through which a page can reach back into the test:
    /// the test installs the callback the page invokes from its first
    /// activation, and reads back the task that call produced.</summary>
    public sealed class SelfDisposeHook
    {
        public Func<ValueTask>? Callback;
        public Task? Captured;
    }

    /// <summary>Page whose OnNavigatedFromAsync blocks on the shared
    /// gate, ignoring cancellation until released — a departure caught
    /// mid-callback by a disposal race.</summary>
    public sealed class DepartureGatedPage : NavigationViewModelBase, INavigationAwareAsync
    {
        private readonly CallbackGate _gate;

        public DepartureGatedPage(PageScopeMarker marker, CallbackGate gate)
        {
            _ = marker;
            _gate = gate;
        }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) =>
            await _gate.WaitAsync();
    }

    /// <summary>Page that calls back into the test hook (installed as a
    /// service DisposeAsync attempt) from within its first
    /// OnNavigatedToAsync — the reentrancy shape the disposal contract
    /// defines special outcomes for.</summary>
    public sealed class SelfDisposePage : NavigationViewModelBase, INavigationAwareAsync
    {
        private readonly SelfDisposeHook _hook;
        private int _called;

        public SelfDisposePage(PageScopeMarker marker, SelfDisposeHook hook)
        {
            _ = marker;
            _hook = hook;
        }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _called, 1) == 0 && _hook.Callback is { } callback)
            {
                _hook.Captured = callback().AsTask();
            }

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Scoped service whose disposal always throws, so its page
    /// scope's disposal fails with a recognizable exception.</summary>
    public sealed class ThrowOnDisposeService : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException("Throwing scope service.");
    }

    /// <summary>Page whose scope contains a throwing disposable.</summary>
    public sealed class ThrowingScopePage : NavigationViewModelBase
    {
        public ThrowingScopePage(PageScopeMarker marker, ThrowOnDisposeService throwing)
        {
            _ = marker;
            _ = throwing;
        }
    }

    private static ServiceProvider BuildProvider(Action<NavigationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        if (configure is null)
        {
            services.AddElegantSeriesFlow();
        }
        else
        {
            services.AddElegantSeriesFlow(configure);
        }

        services.AddSingleton<PageScopeLedger>();
        services.AddScoped<PageScopeMarker>();
        services.AddSingleton<CallbackGate>();
        services.AddSingleton<SelfDisposeHook>();
        services.AddScoped<ThrowOnDisposeService>();
        services.AddTransient<StressPageA>();
        services.AddTransient<StressPageB>();
        services.AddTransient<StressPageC>();
        services.AddTransient<GatedPage>();
        services.AddTransient<DepartureGatedPage>();
        services.AddTransient<SelfDisposePage>();
        services.AddTransient<ThrowingScopePage>();
        return services.BuildServiceProvider();
    }

    private static async Task WaitForScopeBalanceAsync(PageScopeLedger ledger, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var now = ledger.Snapshot();
            if (now.Created == now.Disposed)
            {
                return;
            }

            await Task.Delay(50);
        }

        var end = ledger.Snapshot();
        Assert.Fail($"Page scopes leaked ({because}): created {end.Created}, disposed {end.Disposed}.");
    }

    // ── I1 / I5 / I6: disposal storm ──────────────────────────

    /// <summary>
    /// Workers navigate across two regions while, mid-storm, three
    /// DisposeAsync calls race each other and the in-flight navigation
    /// work. Every call must settle (I5); navigation calls that arrive
    /// after disposal began may only fail with ObjectDisposedException;
    /// once everything settles, every scope ever created has been
    /// disposed (I1), and the entry points reject new work (I6).
    /// </summary>
    [Fact]
    public async Task DisposalStorm_ConcurrentDisposeAsyncAndNavigations_SettleWithEveryScopeDisposed()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();

        var subscriberErrors = new List<Exception>();
        using var subscription = nav.SubscribeSubscriberError(ex =>
        {
            lock (subscriberErrors)
            {
                subscriberErrors.Add(ex);
            }
        });

        string[] regions = ["DStormA", "DStormB"];
        var failures = new List<Exception>();
        var rejected = 0;
        var live = 0;
        var succeeded = 0;

        async Task Worker(int seed)
        {
            var rng = new Random(seed);
            var announced = false;
            try
            {
                // Loop until the disposal rejects a call: the storm
                // cannot "finish early" and degenerate into a sequential
                // run — every worker is still working when disposal
                // begins, and proves it by exiting through the
                // ObjectDisposedException catch below. The iteration cap
                // is a safety net only; the settle bound catches a
                // worker that never gets rejected.
                for (var i = 0; i < 100_000; i++)
                {
                    var region = regions[rng.Next(regions.Length)];
                    var fate = false;
                    switch (rng.Next(8))
                    {
                        case 0:
                            fate = await nav.GoBackAsync(region);
                            break;
                        case 1:
                            await nav.ClearCacheAsync(region);
                            break;
                        default:
                            var retention = rng.Next(3) == 0 ? NavigationRetention.KeepAlive : NavigationRetention.Destroy;
                            fate = rng.Next(3) switch
                            {
                                0 => await nav.NavigateToAsync<StressPageA>(regionName: region, retention: retention),
                                1 => await nav.NavigateToAsync<StressPageB>(regionName: region, retention: retention),
                                _ => await nav.NavigateToAsync<StressPageC>(regionName: region, retention: retention),
                            };
                            break;
                    }

                    if (fate)
                    {
                        Interlocked.Increment(ref succeeded);
                    }

                    if (!announced)
                    {
                        announced = true;
                        Interlocked.Increment(ref live);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // The expected terminal signal once disposal has begun (I6).
                Interlocked.Increment(ref rejected);
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            }
        }

        var workers = Enumerable.Range(0, 6).Select(seed => Task.Run(() => Worker(seed * 104729 + 7))).ToArray();

        // Fire the three racing disposal calls only once every worker
        // is provably live and working — not after a fixed delay.
        var liveDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (Volatile.Read(ref live) < workers.Length && DateTime.UtcNow < liveDeadline)
        {
            await Task.Delay(25);
        }

        Assert.Equal(workers.Length, Volatile.Read(ref live));
        var disposeTasks = new[]
        {
            ((IAsyncDisposable)nav).DisposeAsync().AsTask(),
            ((IAsyncDisposable)nav).DisposeAsync().AsTask(),
            ((IAsyncDisposable)nav).DisposeAsync().AsTask(),
        };

        var everything = Task.WhenAll(workers.Concat(disposeTasks));
        var finished = await Task.WhenAny(everything, Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.True(ReferenceEquals(finished, everything), "The disposal storm did not settle within 60 seconds (hang suspected).");
        await everything;

        // Every worker was still running when disposal began and was
        // rejected by it; the storm did real navigation work first.
        Assert.Equal(workers.Length, Volatile.Read(ref rejected));
        Assert.True(Volatile.Read(ref succeeded) > 0, "The storm completed no successful navigation before disposal.");
        Assert.Empty(failures);
        lock (subscriberErrors)
        {
            Assert.Empty(subscriberErrors);
        }

        await WaitForScopeBalanceAsync(ledger, "after the disposal storm");
        Assert.True(ledger.Snapshot().Created > 0, "Sanity: the storm created no page scopes at all.");

        // I6: entry points reject, queries return empty state, and both
        // disposal forms are safe no-ops now.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await nav.NavigateToAsync<StressPageA>());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await nav.GoBackAsync());
        Assert.Null(nav.GetCurrentViewModel());
        await ((IAsyncDisposable)nav).DisposeAsync();
        ((IDisposable)nav).Dispose();
    }

    // ── I4: shared outcome for concurrent joiners ─────────────

    /// <summary>
    /// A page scope whose disposal throws: three DisposeAsync calls
    /// join the single disposal operation and every joiner observes
    /// the SAME exception instance. The calls are staged while the
    /// operation is provably in flight — a gated page's callback is
    /// still blocked, so the disposal cannot complete before all
    /// three calls have joined (a call arriving only after completion
    /// would return immediately instead, per the contract). A call
    /// after the operation has completed returns immediately.
    /// </summary>
    [Fact]
    public async Task ConcurrentDisposeAsync_ThrowingScopeDisposal_AllJoinersObserveSameException()
    {
        var provider = BuildProvider();
        var nav = provider.GetRequiredService<INavigationService>();
        var gate = provider.GetRequiredService<CallbackGate>();

        Assert.True(await nav.NavigateToAsync<ThrowingScopePage>());

        // Park the disposal: a second region's page is mid-callback,
        // and disposal waits for its gate before releasing any scope.
        var gatedNavigation = nav.NavigateToAsync<GatedPage>(regionName: "Second");
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        var attempts = new[]
        {
            ((IAsyncDisposable)nav).DisposeAsync().AsTask(),
            ((IAsyncDisposable)nav).DisposeAsync().AsTask(),
            ((IAsyncDisposable)nav).DisposeAsync().AsTask(),
        };

        gate.Release();
        Assert.True(await gatedNavigation.WaitAsync(TimeSpan.FromSeconds(10)));

        var observed = new List<Exception>();
        foreach (var attempt in attempts)
        {
            try
            {
                await attempt;
                Assert.Fail("DisposeAsync should have faulted with the scope disposal error.");
            }
            catch (Exception ex)
            {
                observed.Add(ex);
            }
        }

        Assert.Equal(3, observed.Count);
        Assert.Same(observed[0], observed[1]);
        Assert.Same(observed[0], observed[2]);
        Assert.Contains("Throwing scope service.", observed[0].ToString());

        // Completed (faulted) disposal: later calls return immediately,
        // and the service rejects new navigation.
        await ((IAsyncDisposable)nav).DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await nav.NavigateToAsync<StressPageA>());
    }

    // ── Sync Dispose: lock timeout leaves the service untouched ──

    /// <summary>
    /// A synchronous Dispose that cannot acquire the navigation lock
    /// within SyncLockTimeout throws TimeoutException and the service
    /// is NOT disposed (the state machine's "timeout ≠ half-disposed"
    /// row). Staging note: no user-code window holds the lock — in the
    /// pump model, lifecycle callbacks, guards and scope disposals all
    /// run outside it (verified empirically: a Dispose racing a
    /// gate-blocked callback or a slow synchronous ClearCache simply
    /// proceeds) — so, like the directed ClearCache timeout tests in
    /// NavigationServiceCoverageTests, this test holds the lock itself
    /// via reflection. What is pinned is Dispose's side of the row:
    /// zero state change on timeout, full usability afterwards.
    /// </summary>
    [Fact]
    public async Task SyncDispose_NavigationLockHeld_TimesOutAndLeavesServiceUndisposed()
    {
        var provider = BuildProvider(options =>
        {
            options.SyncLockTimeout = TimeSpan.FromMilliseconds(300);
        });
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<StressPageA>());

        var lockField = nav.GetType().GetField("_navigationLock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(lockField);
        var semaphore = (SemaphoreSlim)lockField.GetValue(nav)!;
        semaphore.Wait();
        try
        {
            var ex = Assert.Throws<TimeoutException>(() => ((IDisposable)nav).Dispose());
            Assert.Contains("timed out", ex.Message);
        }
        finally
        {
            semaphore.Release();
        }

        // Not half-disposed: the page is still current and the service
        // still navigates.
        Assert.IsType<StressPageA>(nav.GetCurrentViewModel());
        Assert.True(await nav.NavigateToAsync<StressPageB>());

        await ((IAsyncDisposable)nav).DisposeAsync();
        await WaitForScopeBalanceAsync(ledger, "after timeout-then-dispose");
    }

    // ── DisposeAsync waits for the in-flight gate ─────────────

    /// <summary>
    /// DisposeAsync begins while a navigation callback is blocked on a
    /// gate (ignoring its cancellation token). Disposal must wait for
    /// the gate rather than releasing the scope under the running
    /// callback. Once the gate opens, the racing navigation — whose
    /// entering callback had already begun when disposal started —
    /// completes its landing (fate true), and the shutdown then
    /// releases its scope along with everything else. (Asymmetry,
    /// probed 2026-10-29: a DEPARTURE blocked in its From callback
    /// when a synchronous Dispose wins the race instead completes
    /// false once released — the departure's remaining work is
    /// abandoned at the next phase boundary, while an entering
    /// callback that had already begun is allowed to land.)
    /// </summary>
    [Fact]
    public async Task DisposeAsync_RacingGatedNavigation_WaitsForGate_ThenReleasesEverything()
    {
        var provider = BuildProvider(options =>
        {
            options.DisposalGateTimeout = TimeSpan.FromSeconds(30);
        });
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();
        var gate = provider.GetRequiredService<CallbackGate>();

        var subscriberErrors = new List<Exception>();
        using var subscription = nav.SubscribeSubscriberError(ex =>
        {
            lock (subscriberErrors)
            {
                subscriberErrors.Add(ex);
            }
        });

        var navigation = nav.NavigateToAsync<GatedPage>();
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        var disposal = ((IAsyncDisposable)nav).DisposeAsync().AsTask();

        // The gate is still closed: disposal must be parked on it.
        var early = await Task.WhenAny(disposal, Task.Delay(400));
        Assert.NotSame(disposal, early);

        gate.Release();
        Assert.True(await navigation.WaitAsync(TimeSpan.FromSeconds(10)));
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));

        await WaitForScopeBalanceAsync(ledger, "after gated disposal");
        lock (subscriberErrors)
        {
            Assert.Empty(subscriberErrors);
        }

        // I6 again on this path: rejected entries, empty queries,
        // idempotent disposal calls.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await nav.NavigateToAsync<StressPageA>());
        Assert.Null(nav.GetCurrentViewModel());
        await ((IAsyncDisposable)nav).DisposeAsync();
        ((IDisposable)nav).Dispose();
    }

    // ── Departure asymmetry: sync Dispose wins mid-From ───────

    /// <summary>
    /// The departure half of the entering/departure asymmetry
    /// (disposal-state-machine.md §7.2): a departure is blocked inside
    /// its From callback when a synchronous Dispose wins the race.
    /// Dispose proceeds (callbacks do not hold the navigation lock)
    /// and completes the shutdown; once the gate opens, the abandoned
    /// departure completes FALSE — unlike an entering callback that
    /// had already begun, which is allowed to land.
    /// </summary>
    [Fact]
    public async Task SyncDispose_DepartureBlockedInFrom_DepartureCompletesFalseAfterRelease()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();
        var gate = provider.GetRequiredService<CallbackGate>();

        Assert.True(await nav.NavigateToAsync<StressPageA>());
        Assert.True(await nav.NavigateToAsync<DepartureGatedPage>());

        // The departure of DepartureGatedPage blocks in its From.
        var navigation = nav.NavigateToAsync<StressPageB>();
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        // The synchronous disposal wins the race and completes.
        ((IDisposable)nav).Dispose();
        Assert.Null(nav.GetCurrentViewModel());

        gate.Release();
        Assert.False(await navigation.WaitAsync(TimeSpan.FromSeconds(10)));

        await WaitForScopeBalanceAsync(ledger, "after sync disposal won the departure race");
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await nav.NavigateToAsync<StressPageA>());
    }

    // ── Reentrancy: DisposeAsync from inside a callback ───────

    /// <summary>
    /// A page calls DisposeAsync from within its own lifecycle
    /// callback, before any other disposal exists. The call must not
    /// start a disposal under the running callback and must not throw
    /// synchronously at the call site: it returns a ValueTask faulted
    /// with InvalidOperationException. The service stays alive; the
    /// driver's own DisposeAsync afterwards disposes everything.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_FromWithinCallback_FaultsWithInvalidOperation_AndServiceSurvives()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();
        var hook = provider.GetRequiredService<SelfDisposeHook>();
        hook.Callback = () => ((IAsyncDisposable)nav).DisposeAsync();

        Assert.True(await nav.NavigateToAsync<SelfDisposePage>());
        Assert.NotNull(hook.Captured);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await hook.Captured!);

        // The rejected call changed nothing: the service still works.
        Assert.True(await nav.NavigateToAsync<StressPageA>());
        await ((IAsyncDisposable)nav).DisposeAsync();
        await WaitForScopeBalanceAsync(ledger, "after callback self-dispose attempt");
    }
}
