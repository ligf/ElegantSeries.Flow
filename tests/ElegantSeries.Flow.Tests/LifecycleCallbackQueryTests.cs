using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>
/// Contract tests for <see cref="INavigationService.IsInLifecycleCallback"/>
/// (design doc <c>p2-1-journal-dispose-callback-design.md</c> §3): the query
/// reports the same classification the service itself applies to
/// callback-originated navigation and to disposal from within a callback —
/// true only while a guard / OnNavigatedTo / OnNavigatedFrom invocation of
/// THIS service is running on the calling flow.
/// </summary>
public class LifecycleCallbackQueryTests
{
    [Fact]
    public async Task OutsideCallback_ReportsFalse()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.False(nav.IsInLifecycleCallback);
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        Assert.False(nav.IsInLifecycleCallback);
    }

    [Fact]
    public async Task InsideToGuardAndFrom_ReportsTrue()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<QueryCapturingViewModel>());
        var vm = Assert.IsType<QueryCapturingViewModel>(nav.GetCurrentViewModel());
        Assert.True(vm.QueryInTo);

        // Leaving the page consults its guard, then runs OnNavigatedFrom.
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        Assert.True(vm.QueryInGuard);
        Assert.True(vm.QueryInFrom);

        // The invocation has returned: the same flow reports false again.
        Assert.False(nav.IsInLifecycleCallback);
    }

    [Fact]
    public async Task InsideAsyncCallback_AfterAwait_StillReportsTrue()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<AsyncQueryCapturingViewModel>());
        var vm = Assert.IsType<AsyncQueryCapturingViewModel>(nav.GetCurrentViewModel());
        Assert.True(vm.QueryAfterAwait);
    }

    [Fact]
    public async Task InsideCallback_ForkedFlow_ReportsTrue_OtherInstance_ReportsFalse()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        var otherProvider = BuildProvider();
        using var _o = otherProvider;
        QueryCapturingViewModel.OtherService = otherProvider.GetRequiredService<INavigationService>();
        try
        {
            Assert.True(await nav.NavigateToAsync<QueryCapturingViewModel>());
        }
        finally
        {
            QueryCapturingViewModel.OtherService = null;
        }

        var vm = Assert.IsType<QueryCapturingViewModel>(nav.GetCurrentViewModel());
        // A flow forked from inside the callback carries the context while
        // the invocation is still running.
        Assert.True(vm.QueryInFork);
        // The classification is per-instance: the other service is not
        // invoking a callback, even on this very flow.
        Assert.False(vm.QueryOfOther);
    }

    [Fact]
    public async Task InsideRegionNavigatedHandler_ReportsFalse()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        bool? queryInHandler = null;
        nav.RegionNavigated += (_, _) => queryInHandler = nav.IsInLifecycleCallback;

        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        // Event subscribers are not lifecycle callbacks: the service clears
        // the classification while raising them.
        Assert.False(queryInHandler);
    }

    [Fact]
    public async Task AfterDisposal_DoesNotThrow_AndReportsFalse()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        await ((IAsyncDisposable)nav).DisposeAsync();

        Assert.False(nav.IsInLifecycleCallback);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<QueryCapturingViewModel>();
        services.AddTransient<AsyncQueryCapturingViewModel>();
        return services.BuildServiceProvider();
    }
}

/// <summary>Captures <see cref="INavigationService.IsInLifecycleCallback"/>
/// as read from inside each kind of lifecycle invocation.</summary>
public class QueryCapturingViewModel : NavigationViewModelBase, INavigationAware, INavigationGuard
{
    /// <summary>Another service instance whose query the To callback reads:
    /// it must report false, because the classification is per-instance.</summary>
    public static INavigationService? OtherService { get; set; }

    public bool? QueryInTo { get; private set; }
    public bool? QueryInFrom { get; private set; }
    public bool? QueryInGuard { get; private set; }
    public bool? QueryInFork { get; private set; }
    public bool? QueryOfOther { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        QueryInTo = Navigation!.IsInLifecycleCallback;
        QueryInFork = Task.Run(() => Navigation!.IsInLifecycleCallback).GetAwaiter().GetResult();
        if (OtherService is not null)
            QueryOfOther = OtherService.IsInLifecycleCallback;
    }

    public void OnNavigatedFrom() => QueryInFrom = Navigation!.IsInLifecycleCallback;

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        QueryInGuard = Navigation!.IsInLifecycleCallback;
        return Task.FromResult(true);
    }
}

/// <summary>Captures the query after an await inside an asynchronous
/// lifecycle callback: the invocation — and its classification — spans
/// the callback's asynchronous continuation.</summary>
public class AsyncQueryCapturingViewModel : NavigationViewModelBase, INavigationAwareAsync
{
    public bool? QueryAfterAwait { get; private set; }

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        QueryAfterAwait = Navigation!.IsInLifecycleCallback;
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
