using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>
/// Coverage-focused tests for paths not exercised by the main test suite.
/// </summary>
public class NavigationServiceCoverageTests
{
    private static INavigationService CreateService(Action<NavigationOptions>? configure = null)
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
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        return services.BuildServiceProvider().GetRequiredService<INavigationService>();
    }

    // ── Subscribe* null validation ──────────────────────────────

    [Fact]
    public void SubscribeRegionNavigated_NullHandler_Throws()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.SubscribeRegionNavigated(null!));
    }

    [Fact]
    public void SubscribeViewModelReleased_NullHandler_Throws()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.SubscribeViewModelReleased(null!));
    }

    [Fact]
    public void SubscribeRegionCacheCleared_NullHandler_Throws()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.SubscribeRegionCacheCleared(null!));
    }

    [Fact]
    public void SubscribeSubscriberError_NullHandler_Throws()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.SubscribeSubscriberError(null!));
    }

    // ── NavigationOptions validation ────────────────────────────

    [Fact]
    public void NavigationOptions_DisposalGateTimeout_NonPositive_Throws()
    {
        var options = new NavigationOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DisposalGateTimeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DisposalGateTimeout = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void NavigationOptions_SyncLockTimeout_NonPositive_Throws()
    {
        var options = new NavigationOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.SyncLockTimeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.SyncLockTimeout = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void NavigationOptions_Defaults_ArePositive()
    {
        var options = new NavigationOptions();
        Assert.True(options.DisposalGateTimeout > TimeSpan.Zero);
        Assert.True(options.SyncLockTimeout > TimeSpan.Zero);
    }

    // ── ViewForAttribute ────────────────────────────────────────

    [Fact]
    public void ViewForAttribute_Parameterless_CreatesWithNullViewModelType()
    {
        var attr = new ViewForAttribute();
        Assert.Null(attr.ViewModelType);
        Assert.Equal(ViewModelLifetime.Transient, attr.Lifetime);
    }

    [Fact]
    public void ViewForAttribute_NullViewModelType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ViewForAttribute(null!));
    }

    [Fact]
    public void ViewForAttribute_WithType_SetsProperties()
    {
        var attr = new ViewForAttribute(typeof(TestHomeViewModel))
        {
            Lifetime = ViewModelLifetime.Singleton,
        };
        Assert.Equal(typeof(TestHomeViewModel), attr.ViewModelType);
        Assert.Equal(ViewModelLifetime.Singleton, attr.Lifetime);
    }

    // ── INavigationAwareAsync<TParam> type mismatch ──────────────

    private sealed class StringParamViewModel : NavigationViewModelBase, INavigationAwareAsync<string>
    {
        public string? Received { get; private set; }

        public Task OnNavigatedToAsync(string parameter, CancellationToken cancellationToken)
        {
            Received = parameter;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task NavigateToAsync_WithWrongParamType_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<StringParamViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        // Pass an int where string is expected
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await svc.NavigateToAsync<StringParamViewModel, int>(42));
        Assert.Contains("type mismatch", ex.Message);
    }

    [Fact]
    public async Task NavigateToAsync_WithNullParam_ForNullableType_Works()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<StringParamViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        // Null parameter for string (nullable) should work
        await svc.NavigateToAsync<StringParamViewModel, string?>(null);
        var vm = Assert.IsType<StringParamViewModel>(svc.GetCurrentViewModel("MainRegion"));
        Assert.Null(vm.Received);
    }

    // ── NavigationViewModelBase explicit interface ───────────────

    [Fact]
    public void NavigationViewModelBase_ExplicitInterface_SetsNavigation()
    {
        var vm = new TestHomeViewModel();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        var asInterface = (INavigationViewModel)vm;
        Assert.Null(asInterface.Navigation);

        asInterface.Navigation = svc;
        Assert.Same(svc, asInterface.Navigation);
        Assert.Same(svc, vm.Navigation);

        asInterface.Navigation = null;
        Assert.Null(asInterface.Navigation);
    }

    // ── Subscribe* success paths ────────────────────────────────

    [Fact]
    public async Task SubscribeRegionNavigated_ReceivesEvent_And_Unsubscribes()
    {
        var svc = CreateService();
        var received = new List<(string, INavigationViewModel)>();
        using var sub = svc.SubscribeRegionNavigated((r, vm) => received.Add((r, vm)));

        await svc.NavigateToAsync<TestHomeViewModel>();
        Assert.Single(received);
        Assert.Equal("MainRegion", received[0].Item1);

        sub.Dispose();
        await svc.NavigateToAsync<TestDetailViewModel>();
        Assert.Single(received); // No more events after unsubscribe
    }

    [Fact]
    public void SubscribeViewModelReleased_Unsubscribe_IsIdempotent()
    {
        var svc = CreateService();
        var count = 0;
        var sub = svc.SubscribeViewModelReleased((r, vm) => count++);

        // Double dispose should not throw (Interlocked.Exchange pattern)
        sub.Dispose();
        sub.Dispose();
    }

    [Fact]
    public async Task SubscribeRegionCacheCleared_ReceivesEvent()
    {
        var svc = CreateService();
        var cleared = new List<string>();
        using var sub = svc.SubscribeRegionCacheCleared(r => cleared.Add(r));

        await svc.NavigateToAsync<TestDetailViewModel>();
        await svc.NavigateToAsync<TestHomeViewModel>(retention: NavigationRetention.KeepAlive);
        // Mutual exclusion: the page enters the cache only when popped.
        await svc.GoBackAsync();
        svc.ClearCache("MainRegion");

        Assert.Contains("MainRegion", cleared);
    }

    [Fact]
    public void SubscribeSubscriberError_ReceivesError()
    {
        var svc = CreateService();
        Exception? received = null;
        using var sub = svc.SubscribeSubscriberError(ex => received = ex);

        // Trigger via reflection on the private event
        var field = svc.GetType().GetField("SubscriberError",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var handler = (Action<Exception>?)field.GetValue(svc);
        var testEx = new InvalidOperationException("test");
        handler?.Invoke(testEx);

        Assert.Same(testEx, received);
    }

    // ── Event subscriber exception isolation ────────────────────
    [Fact]
    public async Task RegionNavigated_ThrowingSubscriber_IsIsolatedAndReported()
    {
        var svc = CreateService();
        Exception? reported = null;
        var reportedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            reported = ex;
            reportedTcs.TrySetResult(true);
        });

        // A throwing subscriber should not break navigation or other subscribers
        var goodCount = 0;
        svc.RegionNavigated += (r, vm) => throw new InvalidOperationException("Subscriber boom.");
        svc.RegionNavigated += (r, vm) => goodCount++;

        await svc.NavigateToAsync<TestHomeViewModel>();

        await reportedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(reported);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal(1, goodCount); // Other subscriber still ran
    }

    [Fact]
    public async Task ViewModelReleased_ThrowingSubscriber_IsIsolated()
    {
        var svc = CreateService();
        Exception? reported = null;
        var reportedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            reported = ex;
            reportedTcs.TrySetResult(true);
        });

        svc.ViewModelReleased += (r, vm) => throw new InvalidOperationException("Release boom.");

        await svc.NavigateToAsync<TestHomeViewModel>();
        // Replace mode disposes the old page, firing ViewModelReleased
        await svc.NavigateToAsync<TestDetailViewModel>(operation: NavigationOperation.Replace);

        await reportedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(reported);
    }

    [Fact]
    public async Task RegionCacheCleared_ThrowingSubscriber_IsIsolated()
    {
        var svc = CreateService();
        Exception? reported = null;
        var reportedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            reported = ex;
            reportedTcs.TrySetResult(true);
        });

        svc.RegionCacheCleared += r => throw new InvalidOperationException("Clear boom.");

        await svc.NavigateToAsync<TestDetailViewModel>();
        await svc.NavigateToAsync<TestHomeViewModel>(retention: NavigationRetention.KeepAlive);
        // Mutual exclusion: the page enters the cache only when popped.
        await svc.GoBackAsync();
        svc.ClearCache("MainRegion");

        await reportedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(reported);
    }

    // ── WaitNavigationLock timeout ──────────────────────────────

    [Fact]
    public void SyncApi_WhenNavigationLockHeld_ThrowsTimeoutException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(o => o.SyncLockTimeout = TimeSpan.FromMilliseconds(50));
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        // Hold the private navigation lock via reflection to force the timeout.
        var lockField = svc.GetType().GetField("_navigationLock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(lockField);
        var semaphore = (SemaphoreSlim)lockField.GetValue(svc)!;
        semaphore.Wait();

        try
        {
            var ex = Assert.Throws<TimeoutException>(() => svc.ClearCache("MainRegion"));
            Assert.Contains("timed out", ex.Message);
        }
        finally
        {
            semaphore.Release();
        }
    }

    [Fact]
    public void SyncApi_WhenNavigationLockHeld_ClearAllCache_ThrowsTimeoutException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(o => o.SyncLockTimeout = TimeSpan.FromMilliseconds(50));
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        var lockField = svc.GetType().GetField("_navigationLock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(lockField);
        var semaphore = (SemaphoreSlim)lockField.GetValue(svc)!;
        semaphore.Wait();

        try
        {
            Assert.Throws<TimeoutException>(() => svc.ClearAllCache());
        }
        finally
        {
            semaphore.Release();
        }
    }

    // ── Deferred disposal gate timeout ──────────────────────────

    [Fact]
    public async Task DeferredDisposal_WhenGateTimesOut_ReportsErrorAndDisposesAnyway()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(o => o.DisposalGateTimeout = TimeSpan.FromMilliseconds(100));
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TrackedDisposeService>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        var errorSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            observedError = ex;
            errorSeen.TrySetResult(true);
        });

        // Create a scope with a trackable disposable service.
        var scope = provider.CreateScope();
        var trackable = scope.ServiceProvider.GetRequiredService<TrackedDisposeService>();
        Assert.False(trackable.Disposed);

        // A gate that never completes (simulates a hanging lifecycle callback).
        var hangingGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        // Invoke the private TrackDeferredDisposal via reflection.
        var method = svc.GetType().GetMethod("TrackDeferredDisposal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        // Create a minimal ViewModel for the event notification.
        var vm = new TestHomeViewModel();
        method.Invoke(svc, new object[]
        {
            hangingGate,
            scope,
            vm,
            "MainRegion",
            (Action<Exception>)(ex =>
            {
                try { observedError = ex; errorSeen.TrySetResult(true); }
                catch { }
            })
        });

        // The gate should time out (100ms) and report via the error handler.
        await errorSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observedError);
        Assert.IsType<TimeoutException>(observedError);
        Assert.Contains("Deferred disposal gate timed out", observedError.Message);

        // The scope must still be disposed despite the timeout (poll briefly).
        for (var i = 0; i < 50 && !trackable.Disposed; i++)
        {
            await Task.Delay(100);
        }
        Assert.True(trackable.Disposed);
    }

    /// <summary>
    /// Simple disposable service for tracking scope disposal in tests.
    /// </summary>
    // ── ClearCacheAsync / ClearAllCacheAsync gate timeout ──────

    /// <summary>
    /// KeepAlive page whose <c>OnNavigatedFromAsync</c> hangs until released,
    /// ignoring its cancellation token. Leaving it makes the pump abandon the
    /// callback after the disposal gate timeout; the entry then sits in the
    /// cache with its lifecycle gate still open.
    /// </summary>
    private sealed class HangingLeaveViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public HangingLeaveViewModel(TrackedDisposeService tracked) => Tracked = tracked;

        public TrackedDisposeService Tracked { get; }
        public TaskCompletionSource LeaveGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LeaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            LeaveEntered.TrySetResult();
            // Deliberately ignores the cancellation token.
            await LeaveGate.Task.ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ClearCacheAsync_WhenCachedPageLifecycleHangs_TimesOutReportsAndDisposesAnyway()
        => await RunClearCacheGateTimeoutScenarioAsync(
            svc => svc.ClearCacheAsync("MainRegion").AsTask(),
            "ClearCacheAsync timed out");

    [Fact]
    public async Task ClearAllCacheAsync_WhenCachedPageLifecycleHangs_TimesOutReportsAndDisposesAnyway()
        => await RunClearCacheGateTimeoutScenarioAsync(
            svc => svc.ClearAllCacheAsync().AsTask(),
            "ClearAllCacheAsync timed out");

    private static async Task RunClearCacheGateTimeoutScenarioAsync(
        Func<INavigationService, Task> clearAsync,
        string expectedMessagePrefix)
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(o => o.DisposalGateTimeout = TimeSpan.FromMilliseconds(200));
        services.AddScoped<TrackedDisposeService>();
        services.AddTransient<HangingLeaveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        var timeoutSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            if (ex is TimeoutException && ex.Message.StartsWith(expectedMessagePrefix, StringComparison.Ordinal))
            {
                timeoutSeen.TrySetResult(true);
            }
        });

        await svc.NavigateToAsync<HangingLeaveViewModel>(retention: NavigationRetention.KeepAlive);
        var vm = Assert.IsType<HangingLeaveViewModel>(svc.GetCurrentViewModel());
        Assert.False(vm.Tracked.Disposed);

        try
        {
            // Leave via Replace: the From callback starts and hangs. A second
            // intent registered while it hangs makes the pump wait out the gate
            // timeout and abandon the callback; the departure then commits and
            // the KeepAlive entry lands in the cache with its lifecycle gate
            // still open (the scope is NOT disposed yet).
            var away = svc.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace);
            await vm.LeaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var third = svc.NavigateToAsync<TestDetailViewModel>(operation: NavigationOperation.Replace);
            Assert.True(await third.WaitAsync(TimeSpan.FromSeconds(30)));
            await away.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(vm.Tracked.Disposed);

            // The clear collects the still-open gate, waits out the same
            // timeout, reports it, and releases the scope anyway. (The
            // WaitAsync guards turn a regression of the timeout behavior
            // into a test failure instead of a hung suite.)
            await clearAsync(svc).WaitAsync(TimeSpan.FromSeconds(30));
            await timeoutSeen.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(vm.Tracked.Disposed);
        }
        finally
        {
            // Let the abandoned callback finish so nothing leaks into
            // other tests, even when an assertion above failed.
            vm.LeaveGate.TrySetResult();
        }
    }

    private sealed class TrackedDisposeService : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// Guard that blocks until released, then throws. Used to verify the
    /// fault-observation path when navigation is cancelled mid-guard.
    /// </summary>
    private sealed class ThrowingAfterBlockGuardViewModel : NavigationViewModelBase, INavigationGuard
    {
        public TaskCompletionSource<bool> GuardEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> GuardRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            GuardEntered.TrySetResult(true);
            await GuardRelease.Task.ConfigureAwait(false);
            throw new InvalidOperationException("Guard failed after cancellation.");
        }
    }

    [Fact]
    public async Task NavigateToAsync_WhenCancelledDuringGuard_GuardFaultIsObserved()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ThrowingAfterBlockGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        var errorSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            observedError = ex;
            errorSeen.TrySetResult(true);
        });

        // Navigate to the guarded VM first
        await svc.NavigateToAsync<ThrowingAfterBlockGuardViewModel>();

        using var cts = new CancellationTokenSource();
        var navTask = svc.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token);

        // Wait until the guard is running
        var vm = Assert.IsType<ThrowingAfterBlockGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancel the navigation while the guard is blocked
        cts.Cancel();

        // The navigation should be cancelled
        await Assert.ThrowsAsync<OperationCanceledException>(() => navTask);

        // Release the guard so it throws; the fault should be observed via SubscriberError.
        // This wait asserts eventual observation, not latency: delivery rides
        // thread-pool scheduling, which a fully parallel suite on a small CI
        // runner can delay well past a few seconds.
        vm.GuardRelease.TrySetResult(true);
        await errorSeen.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(observedError);
        Assert.IsType<InvalidOperationException>(observedError);
    }

    /// <summary>
    /// Guard that blocks on the caller's own cancellation token and faults
    /// with <see cref="InvalidOperationException"/> the moment the caller
    /// cancels. Used to verify the report path when the guard's fault —
    /// not the cancellation — is what the pump's guard race surfaces after
    /// the caller has already withdrawn: the fault must still reach
    /// SubscriberError, not the withdrawn caller's abandoned fate.
    /// </summary>
    private sealed class TokenFaultingGuardViewModel : NavigationViewModelBase, INavigationGuard
    {
        public TaskCompletionSource<bool> GuardEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            GuardEntered.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("Guard failed after cancellation.");
            }

            return true;
        }
    }

    [Fact]
    public async Task NavigateToAsync_WhenGuardFaultsAsCallerCancels_FaultIsObserved()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TokenFaultingGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        var errorSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            observedError = ex;
            errorSeen.TrySetResult(true);
        });

        await svc.NavigateToAsync<TokenFaultingGuardViewModel>();

        using var cts = new CancellationTokenSource();
        var navTask = svc.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token);

        var vm = Assert.IsType<TokenFaultingGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancelling the caller makes the guard fault at the same moment
        // the guard race is abandoned. Whichever completion the race
        // surfaces, the caller has withdrawn by the time the fault is
        // delivered — it must be reported, not faulted into the abandoned
        // fate where nobody observes it.
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => navTask);

        await errorSeen.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(observedError);
        Assert.IsType<InvalidOperationException>(observedError);
    }

    /// <summary>
    /// Guard whose task the test faults manually after the guard has
    /// started. Used to deliver a faulted guard task: an
    /// OperationCanceledException reaches the fault path wrapped in an
    /// AggregateException (a bare OCE thrown by an async guard completes
    /// its task as Canceled instead and never faults).
    /// </summary>
    private sealed class ManuallyFaultedGuardViewModel : NavigationViewModelBase, INavigationGuard
    {
        public TaskCompletionSource<bool> GuardEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _guardResult = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Fault(Exception error) => _guardResult.TrySetException(error);

        public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            GuardEntered.TrySetResult(true);
            return _guardResult.Task;
        }
    }

    [Fact]
    public async Task NavigateToAsync_WhenCancelledDuringGuard_GuardOceFaultIsNotReported()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ManuallyFaultedGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<ManuallyFaultedGuardViewModel>();

        using var cts = new CancellationTokenSource();
        var navTask = svc.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token);

        var vm = Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancel while the guard is still running: the pump abandons the
        // guard race and completes the caller as cancelled. Awaiting the
        // navigation guarantees the abandon branch has run.
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => navTask);

        // A cancellation artifact is an outcome, not an error: nothing may
        // reach SubscriberError even though the abandoned guard's task
        // faulted carrying an OperationCanceledException. The report, if
        // one were (incorrectly) made, rides a thread-pool continuation —
        // poll briefly so a misreport fails fast, while the full window
        // elapsing with no report is the pass condition.
        vm.Fault(new AggregateException(new OperationCanceledException()));
        for (var i = 0; i < 60 && observedError is null; i++)
        {
            await Task.Delay(50);
        }

        Assert.Null(observedError);
        Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
    }

    [Fact]
    public async Task NavigateToAsync_WhenCancelledDuringGuard_GuardMixedFaultReportsOnlyRealError()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ManuallyFaultedGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        var errorSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            observedError = ex;
            errorSeen.TrySetResult(true);
        });

        await svc.NavigateToAsync<ManuallyFaultedGuardViewModel>();

        using var cts = new CancellationTokenSource();
        var navTask = svc.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token);

        var vm = Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => navTask);

        // The OCE inner is skipped, the real error is still reported.
        vm.Fault(new AggregateException(
            new OperationCanceledException(),
            new InvalidOperationException("Guard failed after cancellation.")));
        await errorSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidOperationException>(observedError);
    }

    /// <summary>
    /// Guard that faults with an <see cref="OperationCanceledException"/>
    /// wrapping a real error. Used to verify that a wrapped error is not
    /// swallowed as a cancellation outcome: the awaiting caller must
    /// receive the wrapped error itself.
    /// </summary>
    [Fact]
    public async Task NavigateToAsync_WhenGuardFaultsWithOceWrappingRealError_CallerReceivesRealError()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ManuallyFaultedGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<ManuallyFaultedGuardViewModel>();

        var navTask = svc.NavigateToAsync<TestHomeViewModel>();
        var vm = Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Without the unwrap fix this is classified as pure cancellation
        // and the caller observes an OperationCanceledException instead.
        vm.Fault(new OperationCanceledException(
            "Guard cancelled.",
            new InvalidOperationException("Guard real failure.")));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => navTask);
        Assert.Equal("Guard real failure.", thrown.Message);
        Assert.Null(observedError);
        Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
    }

    [Fact]
    public async Task NavigateToAsync_WhenGuardFaultsWithNestedOceChain_CallerReceivesInnermostRealError()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ManuallyFaultedGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<ManuallyFaultedGuardViewModel>();

        var navTask = svc.NavigateToAsync<TestHomeViewModel>();
        var vm = Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Unwrapping must walk the whole OCE chain, not stop at the first
        // inner exception: a naive "inner must be null" rule would either
        // swallow this or deliver the outer wrapper.
        vm.Fault(new OperationCanceledException(
            "Outer.",
            new OperationCanceledException(
                "Inner.",
                new InvalidOperationException("Guard real failure."))));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => navTask);
        Assert.Equal("Guard real failure.", thrown.Message);
        Assert.Null(observedError);
        Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
    }

    [Fact]
    public async Task NavigateToAsync_WhenGuardFaultsWithOceWrappingOce_IsStillPureCancellation()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ManuallyFaultedGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<ManuallyFaultedGuardViewModel>();

        var navTask = svc.NavigateToAsync<TestHomeViewModel>();
        var vm = Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A chain of only cancellations carries no real error: the
        // cancellation outcome stands and nothing is reported. The fate
        // cancellation surfaces as TaskCanceledException, an
        // OperationCanceledException subclass, so the assertion must
        // accept derived types. Note: this test pins the characterization
        // only — the nested-chain test above is the one that discriminates
        // the chain-walking rule.
        vm.Fault(new OperationCanceledException(
            "Outer.",
            new OperationCanceledException("Inner.")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navTask);
        Assert.Null(observedError);
        Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
    }

    [Fact]
    public async Task NavigateToAsync_WhenAbandonedGuardFaultsWithWrappedOce_RealErrorIsReported()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ManuallyFaultedGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        var errorSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscribeSubscriberError(ex =>
        {
            observedError = ex;
            errorSeen.TrySetResult(true);
        });

        await svc.NavigateToAsync<ManuallyFaultedGuardViewModel>();

        using var cts = new CancellationTokenSource();
        var navTask = svc.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token);

        var vm = Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => navTask);

        // The abandoned-guard report path flattens the fault and used to
        // skip OCE leaves outright: a wrapped real error vanished there
        // even though the direct path reported it.
        vm.Fault(new AggregateException(
            new OperationCanceledException(
                "Guard cancelled.",
                new InvalidOperationException("Guard real failure."))));
        await errorSeen.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.IsType<InvalidOperationException>(observedError);
    }

    /// <summary>
    /// Page whose <c>OnNavigatedFromAsync</c> returns a faulted task carrying
    /// an <see cref="OperationCanceledException"/> that wraps a real error.
    /// The faulted task (rather than an async throw) is what delivers the
    /// wrapper itself to the runner: an async method throwing an OCE would
    /// complete its task as Canceled instead, losing the wrapper.
    /// </summary>
    private sealed class WrappedOceFromViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) =>
            Task.FromException(new OperationCanceledException(
                "From cancelled.",
                new InvalidOperationException("From real failure.")));
    }

    [Fact]
    public async Task NavigateToAsync_WhenFromCallbackFaultsWithWrappedOce_CallerReceivesRealError()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<WrappedOceFromViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<WrappedOceFromViewModel>();

        // Leaving the page cancels its lifecycle token first, so the
        // runner's `when (token.IsCancellationRequested)` filter matches
        // and the old code swallowed the wrapped error outright.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.NavigateToAsync<TestHomeViewModel>());
        Assert.Equal("From real failure.", thrown.Message);
        Assert.Null(observedError);
    }

    /// <summary>
    /// Page whose <c>OnNavigatedToAsync</c> returns a faulted task carrying
    /// an <see cref="OperationCanceledException"/> that wraps a real error.
    /// Its token is not cancelled at entry time, so the runner takes the
    /// generic catch rather than the cancellation filter.
    /// </summary>
    private sealed class WrappedOceToViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) =>
            Task.FromException(new OperationCanceledException(
                "To cancelled.",
                new InvalidOperationException("To real failure.")));

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task NavigateToAsync_WhenToCallbackFaultsWithWrappedOce_CallerReceivesRealError()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<WrappedOceToViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        // The old code collected the OCE wrapper itself as the error, so
        // the caller observed an OperationCanceledException.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.NavigateToAsync<WrappedOceToViewModel>());
        Assert.Equal("To real failure.", thrown.Message);
        Assert.Null(observedError);
    }

    [Fact]
    public async Task NavigateToAsync_WhenGuardFaultsWithAggregateContainingWrappedOce_CallerReceivesUnwrappedLeaves()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ManuallyFaultedGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<ManuallyFaultedGuardViewModel>();

        var navTask = svc.NavigateToAsync<TestHomeViewModel>();
        var vm = Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        await vm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A multi-leaf aggregate reaches the generic catch as an aggregate
        // (a single-leaf one would be unwrapped by await). The old code
        // delivered the original aggregate — wrapper included — to the
        // awaiting caller; the new code faults with the unwrapped leaves.
        vm.Fault(new AggregateException(
            new OperationCanceledException(
                "Guard cancelled.",
                new InvalidOperationException("Guard real failure.")),
            new InvalidOperationException("Guard second failure.")));
        var thrown = await Assert.ThrowsAsync<AggregateException>(() => navTask);
        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(thrown.InnerExceptions[0]);
        Assert.Equal("Guard real failure.", thrown.InnerExceptions[0].Message);
        Assert.Equal("Guard second failure.", thrown.InnerExceptions[1].Message);
        Assert.Null(observedError);
        Assert.IsType<ManuallyFaultedGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
    }

    /// <summary>
    /// Page whose synchronous <c>OnNavigatedFrom</c> throws an
    /// <see cref="OperationCanceledException"/> wrapping a real error.
    /// The synchronous runner branches never unwrapped, so the caller
    /// used to observe the wrapper.
    /// </summary>
    private sealed class SyncWrappedOceFromViewModel : NavigationViewModelBase, INavigationAware
    {
        public void OnNavigatedTo(object? parameter)
        {
        }

        public void OnNavigatedFrom() => throw new OperationCanceledException(
            "From cancelled.",
            new InvalidOperationException("Sync from real failure."));
    }

    [Fact]
    public async Task NavigateToAsync_WhenSyncFromCallbackThrowsWrappedOce_CallerReceivesRealError()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncWrappedOceFromViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<SyncWrappedOceFromViewModel>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.NavigateToAsync<TestHomeViewModel>());
        Assert.Equal("Sync from real failure.", thrown.Message);
        Assert.Null(observedError);
    }

    /// <summary>
    /// Page whose synchronous <c>OnNavigatedTo</c> throws an
    /// <see cref="OperationCanceledException"/> wrapping a real error.
    /// </summary>
    private sealed class SyncWrappedOceToViewModel : NavigationViewModelBase, INavigationAware
    {
        public void OnNavigatedTo(object? parameter) => throw new OperationCanceledException(
            "To cancelled.",
            new InvalidOperationException("Sync to real failure."));

        public void OnNavigatedFrom()
        {
        }
    }

    [Fact]
    public async Task NavigateToAsync_WhenSyncToCallbackThrowsWrappedOce_CallerReceivesRealError()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncWrappedOceToViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.NavigateToAsync<SyncWrappedOceToViewModel>());
        Assert.Equal("Sync to real failure.", thrown.Message);
        Assert.Null(observedError);
    }

    /// <summary>
    /// Page whose async <c>OnNavigatedFromAsync</c> returns a faulted task
    /// carrying a multi-leaf aggregate: one leaf is an
    /// <see cref="OperationCanceledException"/> wrapping a real error, the
    /// other is a plain error. The runner's generic catch used to collect
    /// the aggregate as-is.
    /// </summary>
    private sealed class AggregateFromViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) =>
            Task.FromException(new AggregateException(
                new OperationCanceledException(
                    "From cancelled.",
                    new InvalidOperationException("Agg from real failure.")),
                new InvalidOperationException("Agg from second failure.")));
    }

    [Fact]
    public async Task NavigateToAsync_WhenFromCallbackFaultsWithAggregate_CallerReceivesUnwrappedLeaves()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<AggregateFromViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<AggregateFromViewModel>();

        var thrown = await Assert.ThrowsAsync<AggregateException>(() => svc.NavigateToAsync<TestHomeViewModel>());
        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(thrown.InnerExceptions[0]);
        Assert.Equal("Agg from real failure.", thrown.InnerExceptions[0].Message);
        Assert.Equal("Agg from second failure.", thrown.InnerExceptions[1].Message);
        Assert.Null(observedError);
    }

    /// <summary>
    /// CollectErrors is private, so these two tests reach it by reflection
    /// (the file already does this for DisposeScopeAsync). They pin the
    /// helper's shape directly: a cancellation wrapper around an aggregate
    /// must flatten to the real leaves, and an aggregate carrying no real
    /// error must keep the original exception.
    /// </summary>
    [Fact]
    public void CollectErrors_WhenOceWrapsAggregate_FlattensToRealLeaves()
    {
        var method = typeof(NavigationService).GetMethod("CollectErrors", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var errors = new List<Exception>();
        method.Invoke(null, [errors, new OperationCanceledException(
            "Wrapped.",
            new AggregateException(
                new InvalidOperationException("first"),
                new InvalidOperationException("second")))]);
        Assert.Equal(2, errors.Count);
        Assert.Equal("first", errors[0].Message);
        Assert.Equal("second", errors[1].Message);
    }

    [Fact]
    public void CollectErrors_WhenAggregateCarriesNoRealError_KeepsOriginal()
    {
        var method = typeof(NavigationService).GetMethod("CollectErrors", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var original = new AggregateException(
            new OperationCanceledException(),
            new OperationCanceledException());
        var errors = new List<Exception>();
        method.Invoke(null, [errors, original]);
        Assert.Single(errors);
        Assert.Same(original, errors[0]);
    }

    /// <summary>
    /// Page whose async <c>OnNavigatedFromAsync</c> returns a faulted task
    /// carrying an OCE that wraps an aggregate of only OCEs. The From token
    /// is already cancelled when the callback runs, so the runner takes
    /// its cancellation-filter branch: a wrapper carrying no real error
    /// must stay swallowed, not surface as a collected error via the
    /// collector's fallback.
    /// </summary>
    private sealed class PureOceAggregateFromViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) =>
            Task.FromException(new OperationCanceledException(
                "From cancelled.",
                new AggregateException(
                    new OperationCanceledException(),
                    new OperationCanceledException())));
    }

    [Fact]
    public async Task NavigateToAsync_WhenFromCallbackFaultsWithPureOceAggregate_NavigationSucceeds()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<PureOceAggregateFromViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<PureOceAggregateFromViewModel>();

        Assert.True(await svc.NavigateToAsync<TestHomeViewModel>());
        Assert.IsType<TestHomeViewModel>(svc.GetCurrentViewModel("MainRegion"));
        Assert.Null(observedError);
    }

    /// <summary>
    /// Page whose async <c>OnNavigatedFromAsync</c> returns a faulted task
    /// carrying an OCE that wraps a mixed aggregate (one real error, one
    /// OCE leaf). The cancellation-filter branch must collect exactly the
    /// real leaf.
    /// </summary>
    private sealed class MixedOceAggregateFromViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) =>
            Task.FromException(new OperationCanceledException(
                "From cancelled.",
                new AggregateException(
                    new InvalidOperationException("Mixed aggregate real failure."),
                    new OperationCanceledException())));
    }

    [Fact]
    public async Task NavigateToAsync_WhenFromCallbackFaultsWithMixedOceAggregate_CallerReceivesRealLeaf()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<MixedOceAggregateFromViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<MixedOceAggregateFromViewModel>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.NavigateToAsync<TestHomeViewModel>());
        Assert.Equal("Mixed aggregate real failure.", thrown.Message);
        Assert.Null(observedError);
    }

    /// <summary>
    /// Page whose async <c>OnNavigatedToAsync</c> blocks until its token is
    /// cancelled, then faults with an OCE that wraps an aggregate of only
    /// OCEs. The fault is delivered via a <see cref="TaskCompletionSource{TResult}"/>
    /// faulted from the token's cancellation callback: an async method
    /// throwing an OCE would complete its task as Canceled instead, losing
    /// the wrapper. A second navigation registered while To is in flight
    /// makes the pump claim it and cancel this entry's token, which is
    /// exactly the To runner's cancellation-filter branch — the mirror of
    /// the From-side pure-aggregate test.
    /// </summary>
    private sealed class PureOceAggregateToViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public TaskCompletionSource<bool> ToEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            ToEntered.TrySetResult(true);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.TrySetException(new OperationCanceledException(
                "To cancelled.",
                new AggregateException(
                    new OperationCanceledException(),
                    new OperationCanceledException()))));
            return tcs.Task;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task NavigateToAsync_WhenToCallbackFaultsWithPureOceAggregateAfterCancel_NavigationSucceeds()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<PureOceAggregateToViewModel>();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<TestHomeViewModel>();

        // To blocks until its token is cancelled. The hop commits the entry
        // to the stack before the entering step runs To, but the commit is
        // asynchronous, so poll until the entering page is current rather
        // than assuming it is already there.
        var enteringTask = svc.NavigateToAsync<PureOceAggregateToViewModel>();
        PureOceAggregateToViewModel? vm = null;
        for (var i = 0; i < 100 && vm is null; i++)
        {
            vm = svc.GetCurrentViewModel("MainRegion") as PureOceAggregateToViewModel;
            if (vm is null)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(vm);
        await vm.ToEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A second, different-target navigation while To is in flight is
        // claimed by the pump, which cancels the entering entry's token;
        // the To callback then faults with the pure-OCE aggregate.
        var leavingTask = svc.NavigateToAsync<TestDetailViewModel>();

        // With the fallback version of the collector, the aggregate would
        // be collected as an error and the entering navigation would fault
        // with it instead of succeeding.
        Assert.True(await enteringTask.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(await leavingTask.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.IsType<TestDetailViewModel>(svc.GetCurrentViewModel("MainRegion"));
        Assert.Null(observedError);
    }

    /// <summary>
    /// Guard that cancels the caller's own token source and immediately
    /// faults with an OperationCanceledException. By the time the pump
    /// surfaces the guard's exception the candidate is already withdrawn,
    /// which is exactly the constellation that used to divert the OCE to
    /// SubscriberError via FaultOrReport (or report it from the abandon
    /// branch) instead of treating it as the cancelled outcome.
    /// </summary>
    private sealed class SelfCancellingOceGuardViewModel : NavigationViewModelBase, INavigationGuard
    {
        public CancellationTokenSource? CallerCts { get; set; }

        public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            CallerCts!.Cancel();
            return Task.FromException<bool>(new OperationCanceledException());
        }
    }

    [Fact]
    public async Task NavigateToAsync_WhenGuardFaultsWithOceAsCallerCancels_OceIsNotReported()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SelfCancellingOceGuardViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<INavigationService>();

        Exception? observedError = null;
        svc.SubscribeSubscriberError(ex => observedError = ex);

        await svc.NavigateToAsync<SelfCancellingOceGuardViewModel>();

        using var cts = new CancellationTokenSource();
        var vm = Assert.IsType<SelfCancellingOceGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
        vm.CallerCts = cts;

        // Whichever completion the guard race surfaces (the faulted guard
        // or the cancellation), the caller observes cancellation, the page
        // stays, and nothing is reported: every delivery path runs before
        // the navigation task completes.
        var navTask = svc.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => navTask);

        Assert.Null(observedError);
        Assert.IsType<SelfCancellingOceGuardViewModel>(svc.GetCurrentViewModel("MainRegion"));
    }

    // ── ClearCacheAsync / ClearAllCacheAsync gate timeout ────────
    // Note: Gate timeout paths for ClearCacheAsync/ClearAllCacheAsync are
    // difficult to test reliably without hanging the test suite (the hanging
    // lifecycle callback blocks navigation lock acquisition). These rare
    // paths are covered by code review.

    // ── Double-dispose exactly-once ─────────────────────────────

    [Fact]
    public async Task DisposeScopeAsync_Twice_SecondCallIsNoOp()
    {
        var svc = CreateService();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        var method = svc.GetType().GetMethod("DisposeScopeAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var errors = new List<Exception>();
        // First call disposes
        var first = await (ValueTask<bool>)method.Invoke(svc, new object[] { scope, errors })!;
        Assert.True(first);
        // Second call should be no-op (already in _disposedScopes)
        var second = await (ValueTask<bool>)method.Invoke(svc, new object[] { scope, errors })!;
        Assert.False(second);

        Assert.Empty(errors);
    }

    [Fact]
    public void DisposeScopeSync_Twice_SecondCallIsNoOp()
    {
        var svc = CreateService();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        var method = svc.GetType().GetMethod("DisposeScopeSync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var errors = new List<Exception>();
        method.Invoke(svc, new object[] { scope, errors });
        method.Invoke(svc, new object[] { scope, errors });

        Assert.Empty(errors);
    }

    // ── ClearCache reentrancy guard ───────────────────────
    // Mutual exclusion design: ClearCache* throws InvalidOperationException
    // when called from within a page lifecycle callback. Reentrancy is not
    // supported; call it outside the callback instead.

    [Fact]
    public async Task ClearCacheAsync_FromOwnNavigatedFrom_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SelfClearingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var svc = services.BuildServiceProvider().GetRequiredService<INavigationService>();
        SelfClearingViewModel.Service = svc;
        SelfClearingViewModel.ThrownException = null;

        await svc.NavigateToAsync<SelfClearingViewModel>(retention: NavigationRetention.KeepAlive);

        // Navigate away: triggers OnNavigatedFromAsync which reentrantly calls
        // ClearCacheAsync. Must throw InvalidOperationException, not deadlock.
        await svc.NavigateToAsync<TestHomeViewModel>();

        Assert.NotNull(SelfClearingViewModel.ThrownException);
        Assert.IsType<InvalidOperationException>(SelfClearingViewModel.ThrownException);
    }

    [Fact]
    public async Task ClearCache_FromOwnNavigatedFrom_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncSelfClearingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var svc = services.BuildServiceProvider().GetRequiredService<INavigationService>();
        SyncSelfClearingViewModel.Service = svc;
        SyncSelfClearingViewModel.ThrownException = null;

        await svc.NavigateToAsync<SyncSelfClearingViewModel>(retention: NavigationRetention.KeepAlive);

        await svc.NavigateToAsync<TestHomeViewModel>();

        Assert.NotNull(SyncSelfClearingViewModel.ThrownException);
        Assert.IsType<InvalidOperationException>(SyncSelfClearingViewModel.ThrownException);
    }

    /// <summary>
    /// Calls ClearCacheAsync reentrantly from within its own OnNavigatedFromAsync.
    /// Records the InvalidOperationException thrown by the reentrancy guard.
    /// </summary>
    private sealed class SelfClearingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public static INavigationService? Service { get; set; }
        public static Exception? ThrownException { get; set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken ct)
            => Task.CompletedTask;

        public async Task OnNavigatedFromAsync(CancellationToken ct)
        {
            if (Service is not null)
            {
                try
                {
                    await Service.ClearCacheAsync("MainRegion");
                }
                catch (Exception ex)
                {
                    ThrownException = ex;
                }
            }
        }
    }

    /// <summary>
    /// Calls the synchronous ClearCache reentrantly from within OnNavigatedFrom.
    /// Records the InvalidOperationException thrown by the reentrancy guard.
    /// </summary>
    private sealed class SyncSelfClearingViewModel : NavigationViewModelBase, INavigationAware
    {
        public static INavigationService? Service { get; set; }
        public static Exception? ThrownException { get; set; }

        public void OnNavigatedTo(object? parameter) { }

        public void OnNavigatedFrom()
        {
            try
            {
                Service?.ClearCache("MainRegion");
            }
            catch (Exception ex)
            {
                ThrownException = ex;
            }
        }
    }

    [Fact]
    public async Task ClearCache_FromOwnNavigatedTo_ThrowsInvalidOperationException()
    {
        // Reentrancy guard: ClearCacheAsync from within OnNavigatedToAsync
        // throws InvalidOperationException.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<NavigatedToSelfClearingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var svc = services.BuildServiceProvider().GetRequiredService<INavigationService>();
        NavigatedToSelfClearingViewModel.Service = svc;
        NavigatedToSelfClearingViewModel.ThrownException = null;

        await svc.NavigateToAsync<NavigatedToSelfClearingViewModel>(retention: NavigationRetention.KeepAlive);

        Assert.NotNull(NavigatedToSelfClearingViewModel.ThrownException);
        Assert.IsType<InvalidOperationException>(NavigatedToSelfClearingViewModel.ThrownException);

        // The page must still be functional: navigating away works.
        await svc.NavigateToAsync<TestHomeViewModel>();
        Assert.Equal(typeof(TestHomeViewModel), svc.GetCurrentViewModel()?.GetType());
    }

    /// <summary>
    /// Calls ClearCacheAsync reentrantly from within its own OnNavigatedToAsync,
    /// while the page is still the active page on the stack.
    /// Records the InvalidOperationException thrown by the reentrancy guard.
    /// </summary>
    private sealed class NavigatedToSelfClearingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public static INavigationService? Service { get; set; }
        public static Exception? ThrownException { get; set; }

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken ct)
        {
            if (Service is not null)
            {
                try
                {
                    await Service.ClearCacheAsync("MainRegion");
                }
                catch (Exception ex)
                {
                    ThrownException = ex;
                }
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken ct)
            => Task.CompletedTask;
    }
}
