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

    /// <summary>Page whose entering callback parks asynchronously on
    /// the shared gate and only then calls back into the test hook —
    /// the disposal call must be issued from inside the callback while
    /// the marker is active. The park must yield (await), never block
    /// the pump's flow synchronously: the pump starts on the calling
    /// thread up to its first incomplete await, so a synchronous block
    /// inside the callback would deadlock the test against its own
    /// release signal (probed 2026-11-10: a ManualResetEventSlim park
    /// hung the test host until killed).</summary>
    public sealed class EnteringSelfDisposePage : NavigationViewModelBase, INavigationAwareAsync
    {
        private readonly SelfDisposeHook _hook;
        private readonly CallbackGate _gate;
        private int _called;

        public EnteringSelfDisposePage(PageScopeMarker marker, SelfDisposeHook hook, CallbackGate gate)
        {
            _ = marker;
            _hook = hook;
            _gate = gate;
        }

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _called, 1) == 0 && _hook.Callback is { } callback)
            {
                await _gate.WaitAsync();
                _hook.Captured = callback().AsTask();
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

    /// <summary>Page whose <c>OnNavigatedFromAsync</c> always faults
    /// with a recognizable exception, so the hop that departs it stores
    /// that exception in the hop's entered-errors list.</summary>
    public sealed class FromThrowingPage : NavigationViewModelBase, INavigationAwareAsync
    {
        public FromThrowingPage(PageScopeMarker marker)
        {
            _ = marker;
        }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("FromThrowingPage.OnNavigatedFromAsync failed."));
    }

    /// <summary>Gate through which a page scope's synchronous disposal
    /// can be parked: signals when the disposal has been entered and
    /// blocks it until the test releases it.</summary>
    public sealed class BlockingDisposeGate
    {
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
    }

    /// <summary>Scoped service whose synchronous disposal blocks on
    /// the shared gate, so a disposal cleanup can be held in flight
    /// deterministically.</summary>
    public sealed class BlockingDisposeService : IDisposable
    {
        private readonly BlockingDisposeGate _gate;

        public BlockingDisposeService(BlockingDisposeGate gate) => _gate = gate;

        public void Dispose()
        {
            _gate.Entered.Set();
            // Bounded even on a failing run (whose assertions may skip
            // the release): a parked thread-pool thread must not linger
            // for the life of the test process. Passing runs release
            // the gate immediately, long before this bound.
            _gate.Release.Wait(TimeSpan.FromMinutes(5));
        }
    }

    /// <summary>Page whose scope contains the blocking disposable.</summary>
    public sealed class BlockingDisposePage : NavigationViewModelBase
    {
        public BlockingDisposePage(PageScopeMarker marker, BlockingDisposeService blocking)
        {
            _ = marker;
            _ = blocking;
        }
    }

    /// <summary>Observation points for the scope-teardown reentrancy
    /// fixtures: the teardown bodies signal what they saw from inside a
    /// page scope's container disposal.</summary>
    public sealed class ScopeTeardownProbe
    {
        public TaskCompletionSource InnerCallReturned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool SyncInnerCallCompleted;
    }

    /// <summary>Scoped service whose own async teardown calls back into
    /// the service's DisposeAsync and awaits it — the deadlock shape when
    /// the teardown runs under an in-flight service disposal, whose core
    /// is inline-waiting for this teardown to return.</summary>
    public sealed class AsyncTeardownService : IAsyncDisposable
    {
        private readonly INavigationService _navigation;
        private readonly ScopeTeardownProbe _probe;

        public AsyncTeardownService(INavigationService navigation, ScopeTeardownProbe probe)
        {
            _navigation = navigation;
            _probe = probe;
        }

        public async ValueTask DisposeAsync()
        {
            await ((IAsyncDisposable)_navigation).DisposeAsync().ConfigureAwait(false);
            _probe.InnerCallReturned.TrySetResult();
        }
    }

    /// <summary>Scoped service whose synchronous teardown calls the
    /// service's DisposeAsync without awaiting it and records whether the
    /// returned task was already complete.</summary>
    public sealed class SyncTeardownService : IDisposable
    {
        private readonly INavigationService _navigation;
        private readonly ScopeTeardownProbe _probe;

        public SyncTeardownService(INavigationService navigation, ScopeTeardownProbe probe)
        {
            _navigation = navigation;
            _probe = probe;
        }

        public void Dispose()
        {
            var call = ((IAsyncDisposable)_navigation).DisposeAsync();
            _probe.SyncInnerCallCompleted = call.IsCompleted;
        }
    }

    /// <summary>Page whose scope contains the async teardown service.</summary>
    public sealed class AsyncTeardownPage : NavigationViewModelBase
    {
        public AsyncTeardownPage(PageScopeMarker marker, AsyncTeardownService teardown)
        {
            _ = marker;
            _ = teardown;
        }
    }

    /// <summary>Page whose scope contains the sync teardown service.</summary>
    public sealed class SyncTeardownPage : NavigationViewModelBase
    {
        public SyncTeardownPage(PageScopeMarker marker, SyncTeardownService teardown)
        {
            _ = marker;
            _ = teardown;
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
        services.AddSingleton<BlockingDisposeGate>();
        services.AddScoped<ThrowOnDisposeService>();
        services.AddScoped<BlockingDisposeService>();
        services.AddTransient<StressPageA>();
        services.AddTransient<StressPageB>();
        services.AddTransient<StressPageC>();
        services.AddTransient<GatedPage>();
        services.AddTransient<DepartureGatedPage>();
        services.AddTransient<SelfDisposePage>();
        services.AddTransient<EnteringSelfDisposePage>();
        services.AddTransient<ThrowingScopePage>();
        services.AddTransient<FromThrowingPage>();
        services.AddTransient<BlockingDisposePage>();
        services.AddSingleton<ScopeTeardownProbe>();
        services.AddScoped<AsyncTeardownService>();
        services.AddScoped<SyncTeardownService>();
        services.AddTransient<AsyncTeardownPage>();
        services.AddTransient<SyncTeardownPage>();
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

    /// <summary>
    /// The same gated disposal with DisposalGateTimeout =
    /// TimeSpan.MaxValue: the value is inside the option's
    /// documented-valid set (any positive value), but the framework
    /// timer behind Task.Delay cannot represent a wait above ~49.7
    /// days. The effective timeout clamps to that ceiling, so disposal
    /// parks on the gate instead of faulting with
    /// ArgumentOutOfRangeException before any scope is disposed.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WithGateTimeoutAboveTimerCeiling_WaitsForGate_ThenReleasesEverything()
    {
        var provider = BuildProvider(options =>
        {
            options.DisposalGateTimeout = TimeSpan.MaxValue;
        });
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();
        var gate = provider.GetRequiredService<CallbackGate>();

        var navigation = nav.NavigateToAsync<GatedPage>();
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        var disposal = ((IAsyncDisposable)nav).DisposeAsync().AsTask();

        // Parked on the gate, not faulted at the delay construction.
        var early = await Task.WhenAny(disposal, Task.Delay(400));
        Assert.NotSame(disposal, early);

        gate.Release();
        Assert.True(await navigation.WaitAsync(TimeSpan.FromSeconds(10)));
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));

        await WaitForScopeBalanceAsync(ledger, "after ceiling-clamped disposal");
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

    // ── Transfer rows: sync Dispose × in-flight DisposeAsync ──

    /// <summary>
    /// The takeover row of the disposal transfer table (Dispose /
    /// DisposeAsync concurrency remarks): a synchronous Dispose that
    /// arrives while an async disposal is already in flight does NOT
    /// take the cleanup over and does not wait for it — the service
    /// is already marked disposed, so the call returns immediately,
    /// and the async operation keeps its stronger guarantee and
    /// releases every scope itself once its gate opens.
    /// </summary>
    [Fact]
    public async Task SyncDispose_DuringInFlightDisposeAsync_ReturnsWithoutWaiting_AsyncDisposalCompletesCleanup()
    {
        var provider = BuildProvider(options =>
        {
            options.DisposalGateTimeout = TimeSpan.FromSeconds(30);
        });
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();
        var gate = provider.GetRequiredService<CallbackGate>();

        Assert.True(await nav.NavigateToAsync<StressPageA>());

        // Park the async disposal on the second region's gated page:
        // its callback is in flight, so the disposal cannot release
        // any scope until the gate opens.
        var gatedNavigation = nav.NavigateToAsync<GatedPage>(regionName: "Second");
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var disposal = ((IAsyncDisposable)nav).DisposeAsync().AsTask();

        // Wait until the disposal has actually begun: it sets the
        // disposed flag and clears the stacks under the lock before it
        // parks on the gate, so an emptied region proves both.
        var beginDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (nav.GetCurrentViewModel("Second") is not null && DateTime.UtcNow < beginDeadline)
        {
            await Task.Delay(25);
        }

        Assert.Null(nav.GetCurrentViewModel("Second"));

        // The synchronous call returns without waiting for the gate.
        var syncDispose = Task.Run(() => ((IDisposable)nav).Dispose());
        var syncWinner = await Task.WhenAny(syncDispose, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(syncDispose, syncWinner);
        await syncDispose;

        // Nothing was released early and the async operation is still
        // parked: it kept ownership of the cleanup throughout.
        Assert.Equal(0, ledger.Snapshot().Disposed);
        Assert.False(disposal.IsCompleted);

        gate.Release();
        Assert.True(await gatedNavigation.WaitAsync(TimeSpan.FromSeconds(10)));
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForScopeBalanceAsync(ledger, "after sync Dispose raced an in-flight DisposeAsync");
    }

    /// <summary>
    /// The reverse transfer row: a DisposeAsync that arrives while a
    /// synchronous Dispose is still cleaning up returns immediately —
    /// a completed ValueTask — instead of waiting for a cleanup it
    /// can never join. The synchronous cleanup is held in flight by a
    /// page scope whose disposal blocks on a gate.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_DuringSyncDisposeCleanup_ReturnsImmediately()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();
        var gate = provider.GetRequiredService<BlockingDisposeGate>();

        Assert.True(await nav.NavigateToAsync<BlockingDisposePage>());

        // Start the synchronous disposal in the background and wait
        // until it is provably inside the cleanup: the service is
        // marked disposed and the scope disposal is blocked.
        var syncDispose = Task.Run(() => ((IDisposable)nav).Dispose());
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)));

        var asyncCall = ((IAsyncDisposable)nav).DisposeAsync();
        Assert.True(asyncCall.IsCompletedSuccessfully);
        await asyncCall;

        gate.Release.Set();
        await syncDispose.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForScopeBalanceAsync(ledger, "after DisposeAsync arrived during sync cleanup");
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

    // ── DisposeAsync decision table: in-flight, not begun, in-callback ──

    /// <summary>
    /// Decision-table row: a disposal is in flight but cleanup has NOT
    /// begun — the starter is registered while the core still waits for
    /// the navigation lock (held here by the test, the same reflection
    /// hold as the sync-timeout test). A DisposeAsync issued from
    /// within a lifecycle callback in that window faults with the
    /// reentrancy error, exactly as if no disposal had been requested:
    /// the callback could still be the one the cleanup ends up waiting
    /// for, but at this point nothing is waiting on it yet, so the
    /// plain callback rule applies.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_InFlightNotBegun_FromCallback_FaultsWithReentrancyError()
    {
        var provider = BuildProvider(options =>
        {
            options.DisposalGateTimeout = TimeSpan.FromSeconds(30);
        });
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();
        var hook = provider.GetRequiredService<SelfDisposeHook>();
        var gate = provider.GetRequiredService<CallbackGate>();
        hook.Callback = () => ((IAsyncDisposable)nav).DisposeAsync();

        // The page's first activation parks its entering callback on
        // the gate and the pump yields, so the test thread stays free
        // to stage the race; the marker stays active until the gate
        // opens.
        var navigation = nav.NavigateToAsync<EnteringSelfDisposePage>();
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        // Hold the navigation lock so the disposal core cannot claim
        // the service: the starter registers, cleanup does not begin.
        var lockField = nav.GetType().GetField("_navigationLock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(lockField);
        var semaphore = (SemaphoreSlim)lockField.GetValue(nav)!;
        semaphore.Wait();
        Task disposal;
        try
        {
            disposal = ((IAsyncDisposable)nav).DisposeAsync().AsTask();

            // Let the callback make its call now: it must fault.
            gate.Release();
            var captureDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (hook.Captured is null && DateTime.UtcNow < captureDeadline)
            {
                await Task.Delay(10);
            }

            Assert.NotNull(hook.Captured);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await hook.Captured!.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(
                "DisposeAsync cannot be called from within a page lifecycle callback. " +
                "Call it outside the callback instead.",
                ex.Message);
        }
        finally
        {
            semaphore.Release();
        }

        // The first disposal now proceeds and completes, and the
        // navigation whose callback raced it settles either way.
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        await navigation.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForScopeBalanceAsync(ledger, "after in-flight-not-begun callback DisposeAsync");
    }

    // ── Abandoned entering state: queued release events ──────

    /// <summary>
    /// A Replace hop commits — the outgoing page's scope is disposed
    /// inside the hop and its ViewModelReleased is queued for the
    /// entering step — and a disposal starts at the exact moment the
    /// entering state is stored, so the entering step never runs. The
    /// abandon path must still deliver the queued release: the page was
    /// really released, and subscribers must not be left believing it
    /// is still owned. The incoming page never surfaces, the navigation
    /// reports false, and every scope is disposed exactly once.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_AtEnteringStored_DeliversQueuedReleaseOfReplacedPage()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();

        var released = new List<INavigationViewModel>();
        var surfaced = new List<INavigationViewModel>();
        nav.ViewModelReleased += (_, vm) => { lock (released) released.Add(vm); };
        nav.RegionNavigated += (_, vm) => { lock (surfaced) surfaced.Add(vm); };

        Assert.True(await nav.NavigateToAsync<StressPageA>());
        var pageA = nav.GetCurrentViewModel();
        Assert.NotNull(pageA);

        Task? disposal = null;
        var service = (NavigationService)nav;
        service.EnteringStoredHook = () =>
        {
            service.EnteringStoredHook = null;
            disposal = ((IAsyncDisposable)nav).DisposeAsync().AsTask();
        };

        Assert.False(await nav.NavigateToAsync<StressPageB>(operation: NavigationOperation.Replace));
        Assert.NotNull(disposal);
        await disposal!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(pageA!, released);
        Assert.DoesNotContain(typeof(StressPageB), surfaced.Select(vm => vm.GetType()));
        await WaitForScopeBalanceAsync(ledger, "after disposal at entering-stored");
    }

    /// <summary>
    /// The same entering-stored disposal staging, with the departing
    /// page's OnNavigatedFromAsync faulting during the Replace hop: the
    /// hop stores that exception as its entered errors, which the
    /// entering step would have settled into the navigation's fate. The
    /// abandon path completes the fate false instead, so the exception
    /// must be reported through SubscriberError, exactly once, and the
    /// queued release of the departed page is still delivered.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_AtEnteringStored_ReportsEnteredErrorsViaSubscriberError()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();

        var subscriberErrors = new List<Exception>();
        var released = new List<INavigationViewModel>();
        nav.SubscriberError += ex => { lock (subscriberErrors) subscriberErrors.Add(ex); };
        nav.ViewModelReleased += (_, vm) => { lock (released) released.Add(vm); };

        Assert.True(await nav.NavigateToAsync<FromThrowingPage>());
        var pageA = nav.GetCurrentViewModel();
        Assert.NotNull(pageA);

        Task? disposal = null;
        var service = (NavigationService)nav;
        service.EnteringStoredHook = () =>
        {
            service.EnteringStoredHook = null;
            disposal = ((IAsyncDisposable)nav).DisposeAsync().AsTask();
        };

        Assert.False(await nav.NavigateToAsync<StressPageB>(operation: NavigationOperation.Replace));
        Assert.NotNull(disposal);
        await disposal!.WaitAsync(TimeSpan.FromSeconds(10));

        var reported = Assert.Single(subscriberErrors);
        var fromError = Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal("FromThrowingPage.OnNavigatedFromAsync failed.", fromError.Message);
        Assert.Contains(pageA!, released);
        await WaitForScopeBalanceAsync(ledger, "after entered-errors report at entering-stored");
    }

    // ── I5: scope-teardown reentrancy (audit P1-2) ────────────

    /// <summary>
    /// The audit's deadlock: the service disposal core inline-awaits each
    /// page scope's teardown, and a service inside the scope awaits the
    /// service's DisposeAsync from its own DisposeAsync. The call must
    /// return silently (the disposal is already waiting on the teardown),
    /// so the outer disposal completes in bounded time; without the
    /// scope-teardown rule the inner call joins the very operation that
    /// is waiting for it.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_FromWithinPageScopeTeardown_CompletesInsteadOfDeadlocking()
    {
        var provider = BuildProvider();
        var nav = provider.GetRequiredService<INavigationService>();
        var probe = provider.GetRequiredService<ScopeTeardownProbe>();

        Assert.True(await nav.NavigateToAsync<AsyncTeardownPage>("D1Main"));

        var disposal = ((IAsyncDisposable)nav).DisposeAsync().AsTask();
        // The bounded wait turns the pre-fix deadlock into a failure
        // instead of a hung suite.
        await disposal.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(probe.InnerCallReturned.Task.IsCompleted);
    }

    /// <summary>
    /// The same call when a retired Destroy page's scope is torn down
    /// inline in the background with no disposal in flight. The
    /// teardown's DisposeAsync call must return silently there too — it
    /// must not start a whole-service disposal from inside one page's
    /// teardown — and the service stays usable.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_FromInlineRetiredScopeTeardown_ReturnsSilentlyAndServiceSurvives()
    {
        var provider = BuildProvider();
        var nav = provider.GetRequiredService<INavigationService>();
        var probe = provider.GetRequiredService<ScopeTeardownProbe>();

        Assert.True(await nav.NavigateToAsync<AsyncTeardownPage>("D1Main"));
        // Replace retires the page (a Push would only cover it), which is
        // what sends its scope to the deferred teardown.
        Assert.True(await nav.NavigateToAsync<StressPageA>("D1Main", operation: NavigationOperation.Replace));

        await probe.InnerCallReturned.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(await nav.NavigateToAsync<StressPageB>("D1Main"));
        await ((IAsyncDisposable)nav).DisposeAsync();
    }

    /// <summary>
    /// The synchronous teardown path: clearing the cache disposes a
    /// KeepAlive page's scope synchronously, and a service inside it
    /// calls DisposeAsync. The call returns an already-completed task
    /// and starts nothing; the service stays usable.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_FromSyncCacheTeardown_ReturnsSilentlyAndServiceSurvives()
    {
        var provider = BuildProvider();
        var nav = provider.GetRequiredService<INavigationService>();
        var probe = provider.GetRequiredService<ScopeTeardownProbe>();

        Assert.True(await nav.NavigateToAsync<SyncTeardownPage>("D1Main", retention: NavigationRetention.KeepAlive));
        // Only a page that leaves the stack enters the cache, so replace
        // it rather than covering it.
        Assert.True(await nav.NavigateToAsync<StressPageA>("D1Main", operation: NavigationOperation.Replace));

        nav.ClearCache("D1Main");

        Assert.True(probe.SyncInnerCallCompleted);
        Assert.True(await nav.NavigateToAsync<StressPageB>("D1Main"));
        await ((IAsyncDisposable)nav).DisposeAsync();
    }
}
