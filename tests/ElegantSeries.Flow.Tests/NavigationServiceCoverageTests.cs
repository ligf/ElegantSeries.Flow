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
        await svc.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.KeepAlive);
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
        await svc.NavigateToAsync<TestDetailViewModel>(mode: NavigationMode.Replace);

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
        await svc.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.KeepAlive);
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

        await svc.NavigateToAsync<SelfClearingViewModel>(mode: NavigationMode.KeepAlive);

        // Navigate away: triggers OnNavigatedFromAsync which reentrantly calls
        // ClearCacheAsync. Must throw InvalidOperationException, not deadlock.
        await svc.NavigateToAsync<TestHomeViewModel>();

        Assert.NotNull(SelfClearingViewModel.ThrownException);
        Assert.IsType<InvalidOperationException>(SelfClearingViewModel.ThrownException);
    }

    [Fact]
    public void ClearCache_FromOwnNavigatedFrom_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncSelfClearingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var svc = services.BuildServiceProvider().GetRequiredService<INavigationService>();
        SyncSelfClearingViewModel.Service = svc;
        SyncSelfClearingViewModel.ThrownException = null;

        svc.NavigateToAsync<SyncSelfClearingViewModel>(mode: NavigationMode.KeepAlive)
            .GetAwaiter().GetResult();

        svc.NavigateToAsync<TestHomeViewModel>().GetAwaiter().GetResult();

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
    public void ClearCache_FromOwnNavigatedTo_ThrowsInvalidOperationException()
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

        svc.NavigateToAsync<NavigatedToSelfClearingViewModel>(mode: NavigationMode.KeepAlive)
            .GetAwaiter().GetResult();

        Assert.NotNull(NavigatedToSelfClearingViewModel.ThrownException);
        Assert.IsType<InvalidOperationException>(NavigatedToSelfClearingViewModel.ThrownException);

        // The page must still be functional: navigating away works.
        svc.NavigateToAsync<TestHomeViewModel>().GetAwaiter().GetResult();
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
