using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

public class TestHomeViewModel : NavigationViewModelBase
{
    public bool NavigatedToCalled { get; set; }
    public bool NavigatedFromCalled { get; set; }
}

public class ThrowingNavigatedFromViewModel : NavigationViewModelBase, INavigationAware
{
    public bool ThrowOnNavigatedFrom { get; set; }

    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom()
    {
        if (ThrowOnNavigatedFrom)
        {
            throw new InvalidOperationException("OnNavigatedFrom failed.");
        }
    }
}

public class ThrowingNavigatedToViewModel : NavigationViewModelBase, INavigationAware
{
    public void OnNavigatedTo(object? parameter)
        => throw new InvalidOperationException("OnNavigatedTo failed.");

    public void OnNavigatedFrom() { }
}

/// <summary>
/// A ViewModel that navigates again synchronously from inside
/// <see cref="INavigationAware.OnNavigatedTo"/>. If lifecycle callbacks ran under
/// the navigation lock, the nested call would deadlock.
/// </summary>
public class ReentrantNavigatedToViewModel : NavigationViewModelBase, INavigationAware
{
    public void OnNavigatedTo(object? parameter)
    {
        // Deliberately blocking: a lock held across lifecycle callbacks would deadlock here.
        NavigateToAsync<TestHomeViewModel>().GetAwaiter().GetResult();
    }

    public void OnNavigatedFrom() { }
}

/// <summary>
/// Guard that replaces itself via a nested navigation while an outer navigation is
/// awaiting this guard, forcing the outer navigation into the stateMismatch path.
/// </summary>
public class StateMismatchGuardViewModel : NavigationViewModelBase, INavigationGuard
{
    private bool _nestedDone;

    public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        if (!_nestedDone)
        {
            _nestedDone = true;
            // Replace the current page while the outer navigation is awaiting this guard:
            // when the guard returns, the stack top no longer matches and the outer
            // navigation is abandoned (stateMismatch).
            await Navigation!.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.Replace);
        }

        return true;
    }
}

/// <summary>
/// Tracks disposal so tests can observe instances the service resolved but never used.
/// </summary>
public class TrackedDisposeViewModel : NavigationViewModelBase, IDisposable
{
    public static int DisposeCount;

    public void Dispose() => DisposeCount++;
}

public class TestDetailViewModel : NavigationViewModelBase, INavigationAware<string>, IAsyncDisposable
{
    public string? ReceivedParam { get; private set; }
    public bool Disposed { get; private set; }

    public void OnNavigatedTo(string parameter)
    {
        ReceivedParam = parameter;
    }

    public void OnNavigatedFrom() { }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

public class SimpleAwareViewModel : NavigationViewModelBase, INavigationAware
{
    public object? NavigatedToParam { get; private set; }
    public bool NavigatedToCalled { get; private set; }
    public bool NavigatedFromCalled { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        NavigatedToCalled = true;
        NavigatedToParam = parameter;
    }

    public void OnNavigatedFrom()
    {
        NavigatedFromCalled = true;
    }
}

public class CallerViewModel : NavigationViewModelBase
{
    public Task<bool> TriggerNavigateToHomeAsync() => NavigateToAsync<TestHomeViewModel>();
    public Task<bool> TriggerNavigateToDetailAsync(string param) => NavigateToAsync<TestDetailViewModel, string>(param);
    public Task<bool> TriggerGoBackAsync() => GoBackAsync();
}

public class GuardedViewModel : NavigationViewModelBase, INavigationGuard
{
    public bool AllowNavigation { get; set; } = true;

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(AllowNavigation);
    }
}

/// <summary>
/// Counts lifecycle callbacks to verify each fires exactly once.
/// </summary>
public class CountingAwareViewModel : NavigationViewModelBase, INavigationAware
{
    public int NavigatedToCount { get; private set; }
    public int NavigatedFromCount { get; private set; }

    public void OnNavigatedTo(object? parameter) => NavigatedToCount++;
    public void OnNavigatedFrom() => NavigatedFromCount++;
}

public class KeepAliveViewModel : NavigationViewModelBase, IDisposable
{
    public bool Disposed { get; private set; }
    public int DisposeCallCount { get; private set; }

    public void Dispose()
    {
        DisposeCallCount++;
        Disposed = true;
    }
}

public sealed class ParamAwareKeepAliveViewModel : NavigationViewModelBase, INavigationAware, IDisposable
{
    public object? LastParameter { get; private set; }
    public bool Disposed { get; private set; }

    public void OnNavigatedTo(object? parameter) => LastParameter = parameter;

    public void OnNavigatedFrom()
    {
    }

    public void Dispose() => Disposed = true;
}

/// <summary>
/// A KeepAlive ViewModel whose synchronous <see cref="IDisposable.Dispose"/> always throws.
/// Used to verify that one failing disposal never blocks the disposal of the remaining ViewModels.
/// </summary>
public class ThrowingKeepAliveViewModel : NavigationViewModelBase, IDisposable
{
    public bool DisposeAttempted { get; private set; }

    public void Dispose()
    {
        DisposeAttempted = true;
        throw new InvalidOperationException("Sync dispose failed.");
    }
}

/// <summary>
/// A KeepAlive ViewModel whose <see cref="IAsyncDisposable.DisposeAsync"/> always throws.
/// </summary>
public class ThrowingAsyncDisposeViewModel : NavigationViewModelBase, IAsyncDisposable
{
    public bool DisposeAttempted { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeAttempted = true;
        throw new InvalidOperationException("Async dispose failed.");
    }
}

public class TestView { }

public class NavigationServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;

    public NavigationServiceTests()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        services.AddTransient<SimpleAwareViewModel>();
        services.AddTransient<CallerViewModel>();
        services.AddTransient<GuardedViewModel>();
        services.AddTransient<ThrowingNavigatedFromViewModel>();
        services.AddTransient<CountingAwareViewModel>();
        services.AddTransient<ThrowingNavigatedToViewModel>();
        services.AddTransient<ReentrantNavigatedToViewModel>();
        services.AddTransient<StateMismatchGuardViewModel>();
        services.AddTransient<TrackedDisposeViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        services.AddTransient<ThrowingKeepAliveViewModel>();
        services.AddTransient<ParamAwareKeepAliveViewModel>();
        services.AddTransient<ThrowingAsyncDisposeViewModel>();
        services.AddTransient<DisposableService>();
        services.AddTransient<ServiceDependentViewModel>();
        services.AddTransient<ScopedViewModel>();
        services.AddTransient<ThrowingDependencyViewModel>();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<RefreshableViewModel>();
        services.AddTransient<ConstructionCountingViewModel>();
        services.AddTransient<BothAwareViewModel>();
        services.AddTransient<AsyncThrowingViewModel>();
        services.AddTransient<TypedAsyncViewModel>();
        services.AddTransient<ContextGuardViewModel>();
        services.AddTransient<BothGuardViewModel>();
        services.AddTransient<ContextGuardDetailViewModel>();
        services.AddTransient<CancelInNavigatedToViewModel>();
        services.AddTransient<ThinViewModel>();

        _serviceProvider = services.BuildServiceProvider();
        _navigationService = _serviceProvider.GetRequiredService<INavigationService>();
    }

    [Fact]
    public async Task NavigateToAsync_ShouldActivateViewModel_AndTriggerEvent()
    {
        string? notifiedRegion = null;
        INavigationViewModel? notifiedVm = null;

        _navigationService.RegionNavigated += (r, vm) =>
        {
            notifiedRegion = r;
            notifiedVm = vm;
        };

        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion");

        Assert.True(result);
        Assert.Equal("MainRegion", notifiedRegion);
        Assert.IsType<TestHomeViewModel>(notifiedVm);
        Assert.NotNull(notifiedVm.Navigation);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>("MainRegion"));
    }

    [Fact]
    public async Task NavigateToAsync_WithParameter_ShouldPassTypedParameter()
    {
        var result = await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Hello Navigation");

        Assert.True(result);
        var current = _navigationService.GetCurrentViewModel() as TestDetailViewModel;
        Assert.NotNull(current);
        Assert.Equal("Hello Navigation", current.ReceivedParam);
    }

    [Fact]
    public async Task NavigateToAsync_WithNullParameter_ShouldPassNullToAware()
    {
        var result = await _navigationService.NavigateToAsync<TestDetailViewModel, string?>(null);

        Assert.True(result);
        var current = _navigationService.GetCurrentViewModel() as TestDetailViewModel;
        Assert.NotNull(current);
        Assert.Null(current.ReceivedParam);
    }

    [Fact]
    public async Task SimpleAwareViewModel_ShouldTriggerNavigatedToAndFrom()
    {
        var navResult = await _navigationService.NavigateToAsync<SimpleAwareViewModel, int>(42);
        Assert.True(navResult);

        var simple = _navigationService.GetCurrentViewModel() as SimpleAwareViewModel;
        Assert.NotNull(simple);
        Assert.True(simple.NavigatedToCalled);
        Assert.Equal(42, simple.NavigatedToParam);

        // 切出到新页面
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.True(simple.NavigatedFromCalled);
    }

    [Fact]
    public async Task GoBackAsync_ShouldReturnToPreviousViewModel_AndDisposeCurrent()
    {
        INavigationViewModel? disposedVm = null;
        _navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Arg");

        Assert.True(_navigationService.CanGoBack());

        var goBackResult = await _navigationService.GoBackAsync();
        Assert.True(goBackResult);

        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        Assert.IsType<TestDetailViewModel>(disposedVm);
        Assert.True(((TestDetailViewModel)disposedVm).Disposed);
    }

    [Fact]
    public async Task NavigationGuard_WhenDisallowed_ShouldCancelNavigation()
    {
        await _navigationService.NavigateToAsync<GuardedViewModel>();
        var guarded = (GuardedViewModel)_navigationService.GetCurrentViewModel()!;
        guarded.AllowNavigation = false;

        var navResult = await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.False(navResult);
        Assert.True(_navigationService.IsActive<GuardedViewModel>());

        guarded.AllowNavigation = true;
        var retryResult = await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.True(retryResult);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task OnNavigatedFromThrows_ShouldCompleteTransitionAndPropagateException()
    {
        await _navigationService.NavigateToAsync<ThrowingNavigatedFromViewModel>();
        var oldViewModel = (ThrowingNavigatedFromViewModel)_navigationService.GetCurrentViewModel()!;
        oldViewModel.ThrowOnNavigatedFrom = true;

        INavigationViewModel? navigatedEventViewModel = null;
        _navigationService.RegionNavigated += (_, vm) => navigatedEventViewModel = vm;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<TestHomeViewModel>());

        Assert.Equal("OnNavigatedFrom failed.", exception.Message);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        Assert.Null(oldViewModel.Navigation);
        Assert.IsType<TestHomeViewModel>(navigatedEventViewModel);
    }

    [Fact]
    public async Task OnNavigatedToThrows_ShouldKeepCommittedPageActiveAndPropagateException()
    {
        INavigationViewModel? navigatedEventViewModel = null;
        _navigationService.RegionNavigated += (_, vm) => navigatedEventViewModel = vm;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<ThrowingNavigatedToViewModel>());

        var current = _navigationService.GetCurrentViewModel();
        Assert.Equal("OnNavigatedTo failed.", exception.Message);
        Assert.IsType<ThrowingNavigatedToViewModel>(current);
        Assert.Same(current, navigatedEventViewModel);
        Assert.Same(_navigationService, current!.Navigation);
    }

    [Fact]
    public async Task KeepAliveMode_ShouldReuseSameInstance_AndNotDisposeOnGoBack()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);

        var firstInstance = _navigationService.GetCurrentViewModel();
        Assert.IsType<KeepAliveViewModel>(firstInstance);

        // 返回 Home
        await _navigationService.GoBackAsync();
        Assert.False(((KeepAliveViewModel)firstInstance).Disposed);

        // 再次导航到 KeepAlive
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        var secondInstance = _navigationService.GetCurrentViewModel();

        Assert.Same(firstInstance, secondInstance);

        // 清理缓存后应销毁
        await _navigationService.ClearCacheAsync("MainRegion");
        Assert.False(((KeepAliveViewModel)firstInstance).Disposed);

        await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion", NavigationMode.ClearStack);
        Assert.True(((KeepAliveViewModel)firstInstance).Disposed);
        Assert.Equal(1, ((KeepAliveViewModel)firstInstance).DisposeCallCount);
    }

    [Fact]
    public async Task NewMode_WhenNavigatingAwayAndBack_ShouldCreateFreshInstance_AndDisposeOldOne()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first");

        var firstInstance = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Equal("first", firstInstance.ReceivedParam);

        // Leave the page: the transient page scope is disposed.
        await _navigationService.GoBackAsync();
        Assert.True(firstInstance.Disposed);

        // Come back: a brand-new instance, no state carried over.
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("second");
        var secondInstance = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.NotSame(firstInstance, secondInstance);
        Assert.False(secondInstance.Disposed);
        Assert.Equal("second", secondInstance.ReceivedParam);
    }

    [Fact]
    public async Task ClearCache_WhenKeepAliveIsInHistory_ShouldDisposeAfterLastStackReferenceIsRemoved()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        await _navigationService.ClearCacheAsync("MainRegion");
        Assert.False(keepAlive.Disposed);

        Assert.True(await _navigationService.GoBackAsync());
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
        Assert.False(keepAlive.Disposed);

        await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion", NavigationMode.ClearStack);
        Assert.True(keepAlive.Disposed);
        Assert.Equal(1, keepAlive.DisposeCallCount);
    }

    [Fact]
    public async Task KeepAlive_WhenCachedInstanceDeeperOnStack_PopsBackToIt()
    {
        // Mainstream behavior: navigating to a cached KeepAlive page whose instance is
        // already on the stack pops back to it instead of pushing a duplicate.
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestDetailViewModel>();
        var detail = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;

        int navigatedEvents = 0;
        _navigationService.RegionNavigated += (_, _) => navigatedEvents++;

        var result = await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);

        Assert.True(result);
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
        // The Detail page was popped; the Home page below remains.
        Assert.True(_navigationService.CanGoBack());
        Assert.Equal(1, navigatedEvents);

        // The popped Detail page's scope was disposed; the shared KeepAlive scope survives.
        Assert.True(detail.Disposed);
        Assert.False(keepAlive.Disposed);

        // The page is still cached and reusable afterwards.
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive));
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
        Assert.False(keepAlive.Disposed);
    }

    [Fact]
    public async Task NavigateToAsync_PopToExisting_DeliversNewParameter()
    {
        await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", mode: NavigationMode.KeepAlive);
        var vm = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        var result = await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "second", mode: NavigationMode.KeepAlive);

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal("second", vm.LastParameter);
    }

    [Fact]
    public async Task NavigateToAsync_WhenSingletonOnAnotherRegionStack_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<SingletonViewModel>("Region1");

        // Same-region would pop back to the instance; another region cannot share it:
        // a ViewModel has a single Navigation reference.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => navigationService.NavigateToAsync<SingletonViewModel>("Region2"));

        Assert.Contains("another region's", ex.Message);
        Assert.Null(navigationService.GetCurrentViewModel("Region2"));
        Assert.True(navigationService.IsActive<SingletonViewModel>("Region1"));

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ClearCache_WhenSingletonCachedInTwoRegions_ShouldNeverDisposeIt()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<KeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<TestHomeViewModel>("Region1");
        await navigationService.NavigateToAsync<KeepAliveViewModel>("Region1", NavigationMode.KeepAlive);
        var keepAlive = (KeepAliveViewModel)navigationService.GetCurrentViewModel("Region1")!;
        await navigationService.GoBackAsync("Region1");

        await navigationService.NavigateToAsync<TestHomeViewModel>("Region2");
        await navigationService.NavigateToAsync<KeepAliveViewModel>("Region2", NavigationMode.KeepAlive);
        Assert.Same(keepAlive, navigationService.GetCurrentViewModel("Region2"));

        await navigationService.ClearCacheAsync("Region1");

        // A singleton belongs to the root container. Clearing a region cache must never
        // dispose it, even when its page scope is torn down.
        Assert.False(keepAlive.Disposed);

        await navigationService.GoBackAsync("Region2");
        await navigationService.NavigateToAsync<KeepAliveViewModel>("Region2", NavigationMode.KeepAlive);
        Assert.Same(keepAlive, navigationService.GetCurrentViewModel("Region2"));
        Assert.False(keepAlive.Disposed);

        await navigationService.DisposeAsync();
        Assert.False(keepAlive.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task NavigateToAsync_WhenStackChangesDuringGuard_ShouldDisposeAbandonedPageScope()
    {
        TrackedDisposeViewModel.DisposeCount = 0;

        await _navigationService.NavigateToAsync<StateMismatchGuardViewModel>();
        var result = await _navigationService.NavigateToAsync<TrackedDisposeViewModel>();

        // The navigation was abandoned: the guard's nested Replace won the race.
        Assert.False(result);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        // The pre-resolved but never used Transient instance is disposed by the service.
        Assert.Equal(1, TrackedDisposeViewModel.DisposeCount);
    }

    [Fact]
    public async Task ClearCacheAsync_ThenGoBack_ShouldFireDeferredDisposalOfPoppedKeepAliveViewModel()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.ClearCacheAsync("MainRegion");

        // Still referenced by the navigation stack: disposal is deferred, not executed.
        Assert.False(keepAlive.Disposed);
        Assert.True(_navigationService.IsActive<KeepAliveViewModel>());

        Assert.True(await _navigationService.GoBackAsync());

        // Popping the last reference fires the deferred disposal exactly once.
        Assert.True(keepAlive.Disposed);
        Assert.Equal(1, keepAlive.DisposeCallCount);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task ReplaceMode_ShouldReplaceCurrentPage()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Param1");
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.Replace);

        Assert.True(_navigationService.IsActive<KeepAliveViewModel>());
        Assert.True(_navigationService.CanGoBack());

        // 返回应直接回到 HomeViewModel，跳过被 Replace 的 DetailViewModel
        await _navigationService.GoBackAsync();
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task ClearStackMode_ShouldEmptyStackAndPushNew()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Param");

        Assert.True(_navigationService.CanGoBack());

        await _navigationService.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.ClearStack);

        Assert.False(_navigationService.CanGoBack());
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task GetCurrentMode_And_GetCurrentViewModel_ShouldReflectState()
    {
        Assert.Null(_navigationService.GetCurrentMode());
        Assert.Null(_navigationService.GetCurrentViewModel());
        Assert.False(_navigationService.CanGoBack());
        Assert.False(_navigationService.IsActive<TestHomeViewModel>());

        await _navigationService.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.New);
        Assert.Equal(NavigationMode.New, _navigationService.GetCurrentMode());
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        Assert.False(_navigationService.CanGoBack());

        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        Assert.Equal(NavigationMode.KeepAlive, _navigationService.GetCurrentMode());
        Assert.IsType<KeepAliveViewModel>(_navigationService.GetCurrentViewModel());
        Assert.True(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task GoBackAsync_WhenStackHasSingleOrNoItem_ShouldReturnFalse()
    {
        // 空栈
        var emptyBack = await _navigationService.GoBackAsync("EmptyRegion");
        Assert.False(emptyBack);

        // 仅有一个页面
        await _navigationService.NavigateToAsync<TestHomeViewModel>("SingleRegion");
        var singleBack = await _navigationService.GoBackAsync("SingleRegion");
        Assert.False(singleBack);
    }

    [Fact]
    public async Task ClearCache_Sync_ShouldDisposeAndTriggerEvents()
    {
        string? clearedRegion = null;
        INavigationViewModel? disposedVm = null;

        _navigationService.RegionCacheCleared += r => clearedRegion = r;
        _navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        _navigationService.ClearCache("MainRegion");

        Assert.Equal("MainRegion", clearedRegion);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Null(disposedVm);
        Assert.False(keepAlive.Disposed);

        await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion", NavigationMode.ClearStack);
        Assert.Same(keepAlive, disposedVm);
        Assert.True(keepAlive.Disposed);
    }

    [Fact]
    public async Task ClearAllCacheAsync_ShouldDisposeAllCachedInstances()
    {
        var clearedRegions = new HashSet<string>();
        int disposedCount = 0;

        _navigationService.RegionCacheCleared += r => clearedRegions.Add(r);
        _navigationService.ViewModelReleased += (_, _) => disposedCount++;

        await _navigationService.NavigateToAsync<KeepAliveViewModel>("Region1", NavigationMode.KeepAlive);
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("Region2", NavigationMode.KeepAlive);

        await _navigationService.ClearAllCacheAsync();

        Assert.Contains("Region1", clearedRegions);
        Assert.Contains("Region2", clearedRegions);
        Assert.Equal(0, disposedCount);

        await _navigationService.NavigateToAsync<TestHomeViewModel>("Region1", NavigationMode.ClearStack);
        await _navigationService.NavigateToAsync<TestHomeViewModel>("Region2", NavigationMode.ClearStack);
        Assert.Equal(2, disposedCount);
    }

    [Fact]
    public async Task Dispose_Sync_ShouldDisposeStacksAndCaches()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        var sp = services.BuildServiceProvider();
        var navService = (NavigationService)sp.GetRequiredService<INavigationService>();

        await navService.NavigateToAsync<TestHomeViewModel>();
        await navService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        var keepAlive = (KeepAliveViewModel)navService.GetCurrentViewModel()!;
        await navService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);

        bool disposedEventTriggered = false;
        navService.ViewModelReleased += (_, _) => disposedEventTriggered = true;

        navService.Dispose();
        Assert.True(disposedEventTriggered);
        Assert.Equal(1, keepAlive.DisposeCallCount);

        // 验证已释放后再次调用是幂等的
        navService.Dispose();
        await navService.DisposeAsync();

        // 验证已释放后调用导航抛出 ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => navService.NavigateToAsync<TestHomeViewModel>());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => navService.GoBackAsync());
    }

    [Fact]
    public async Task BaseViewModel_ProtectedMethods_ShouldWorkWhenNavAttached_AndGracefullyFallback()
    {
        // 1. 当未附加 Navigation 时，调用方法优雅回退 false
        var detachedCaller = new CallerViewModel();
        Assert.False(await detachedCaller.TriggerNavigateToHomeAsync());
        Assert.False(await detachedCaller.TriggerNavigateToDetailAsync("test"));
        Assert.False(await detachedCaller.TriggerGoBackAsync());

        // 2. 当附加 Navigation 时，通过 CallerViewModel 内部发起导航
        await _navigationService.NavigateToAsync<CallerViewModel>();
        var attachedCaller = (CallerViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotNull(attachedCaller.Navigation);

        var navResult = await attachedCaller.TriggerNavigateToDetailAsync("from-caller");
        Assert.True(navResult);
        Assert.True(_navigationService.IsActive<TestDetailViewModel>());

        // 3. 切出到新页面后，老页面的 Navigation 引用被置空（无法在切出状态下越权导航）
        Assert.Null(attachedCaller.Navigation);
        Assert.False(await attachedCaller.TriggerGoBackAsync());

        // 4. 通过导航服务退栈回到 CallerViewModel，Navigation 重新注入
        await _navigationService.GoBackAsync();
        Assert.True(_navigationService.IsActive<CallerViewModel>());
        Assert.NotNull(attachedCaller.Navigation);

        // 5. 重新激活后可继续正常导航
        var homeNav = await attachedCaller.TriggerNavigateToHomeAsync();
        Assert.True(homeNav);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task AddElegantSeriesFlow_Scoped_ShouldIsolateNavigationStacksBetweenScopes()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(ServiceLifetime.Scoped);
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var sp = services.BuildServiceProvider();

        await using var scope1 = sp.CreateAsyncScope();
        await using var scope2 = sp.CreateAsyncScope();

        var nav1 = scope1.ServiceProvider.GetRequiredService<INavigationService>();
        var nav2 = scope2.ServiceProvider.GetRequiredService<INavigationService>();

        Assert.NotSame(nav1, nav2);

        await nav1.NavigateToAsync<TestHomeViewModel>();
        await nav2.NavigateToAsync<TestDetailViewModel, string>("scope2");

        Assert.True(nav1.IsActive<TestHomeViewModel>());
        Assert.False(nav1.IsActive<TestDetailViewModel>());

        Assert.True(nav2.IsActive<TestDetailViewModel>());
        Assert.False(nav2.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public void ViewForAttribute_ShouldStoreViewModelType_AndThrowOnNull()
    {
        var attr = new ViewForAttribute(typeof(TestHomeViewModel));
        Assert.Equal(typeof(TestHomeViewModel), attr.ViewModelType);

        Assert.Throws<ArgumentNullException>(() => new ViewForAttribute(null!));
    }

    [Fact]
    public void ServiceCollectionExtensions_ShouldThrowOnNullServices()
    {
        Assert.Throws<ArgumentNullException>(() => FlowServiceCollectionExtensions.AddElegantSeriesFlow(null!));
        Assert.Throws<ArgumentNullException>(() => FlowServiceCollectionExtensions.AddElegantSeriesFlow(null!, ServiceLifetime.Scoped));
    }

    [Fact]
    public void AddElegantSeriesFlow_ShouldThrowOnTransientLifetime()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddElegantSeriesFlow(ServiceLifetime.Transient));
    }

    [Fact]
    public void AddElegantSeriesFlow_ShouldThrowOnUndefinedLifetime()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddElegantSeriesFlow((ServiceLifetime)999));
    }

    [Fact]
    public async Task Validation_AllMethods_ShouldThrowOnInvalidRegionName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.NavigateToAsync<TestHomeViewModel>(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.NavigateToAsync<TestDetailViewModel, string>("param", "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.GoBackAsync("  "));
        Assert.Throws<ArgumentException>(() => _navigationService.CanGoBack(""));
        Assert.Throws<ArgumentException>(() => _navigationService.GetCurrentViewModel("   "));
        Assert.Throws<ArgumentException>(() => _navigationService.IsActive<TestHomeViewModel>(""));
        Assert.Throws<ArgumentException>(() => _navigationService.GetCurrentMode("  "));
        Assert.Throws<ArgumentException>(() => _navigationService.ClearCache(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.ClearCacheAsync("  ").AsTask());
    }

    [Fact]
    public async Task NavigateToAsync_ReentrancyInRegionNavigated_ShouldNotDeadlock()
    {
        bool secondNavigated = false;

        void Handler(string region, INavigationViewModel vm)
        {
            if (vm is TestHomeViewModel)
            {
                // In RegionNavigated callback, trigger another navigation (re-entrancy test)
                _ = Task.Run(async () =>
                {
                    await _navigationService.NavigateToAsync<TestDetailViewModel, string>("nested");
                    secondNavigated = true;
                });
            }
        }

        _navigationService.RegionNavigated += Handler;
        try
        {
            var firstNav = await _navigationService.NavigateToAsync<TestHomeViewModel>();
            Assert.True(firstNav);

            // Wait briefly for nested task completion
            for (int i = 0; i < 50 && !secondNavigated; i++)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(secondNavigated);
            Assert.True(_navigationService.IsActive<TestDetailViewModel>());
        }
        finally
        {
            _navigationService.RegionNavigated -= Handler;
        }
    }

    [Fact]
    public async Task NavigateToAsync_WhenOnNavigatedToNavigatesAgain_ShouldNotDeadlock()
    {
        // Run the outer navigation on a worker thread: a deadlock inside OnNavigatedTo
        // surfaces as a timeout here instead of hanging the whole test suite.
        var outerTask = Task.Run(() => _navigationService.NavigateToAsync<ReentrantNavigatedToViewModel>());

        var winner = await Task.WhenAny(outerTask, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Same(outerTask, winner);

        Assert.True(await outerTask);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task ClearCacheAsync_ShouldRaiseRegionCacheCleared_AndViewModelReleasedEvents()
    {
        string? clearedRegion = null;
        INavigationViewModel? disposedVm = null;

        _navigationService.RegionCacheCleared += r => clearedRegion = r;
        _navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await _navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        await _navigationService.ClearCacheAsync("MainRegion");

        Assert.Equal("MainRegion", clearedRegion);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Null(disposedVm);
        Assert.False(keepAlive.Disposed);

        await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion", NavigationMode.ClearStack);
        Assert.Same(keepAlive, disposedVm);
        Assert.True(keepAlive.Disposed);
    }

    [Fact]
    public void NavigationAware_TypeMismatch_ShouldThrowArgumentExceptionWithEnglishMessage()
    {
        INavigationAware aware = new TestDetailViewModel();
        var ex = Assert.Throws<ArgumentException>(() => aware.OnNavigatedTo(12345));

        Assert.Contains("Navigation parameter type mismatch: expected String, but got Int32.", ex.Message);
    }

    [Fact]
    public async Task DisposeAsync_ShouldRaiseEventsOutOfLock_AndNotDeadlock()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<KeepAliveViewModel>();
        var sp = services.BuildServiceProvider();
        var navService = (NavigationService)sp.GetRequiredService<INavigationService>();

        await navService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        var keepAlive = (KeepAliveViewModel)navService.GetCurrentViewModel()!;
        await navService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);

        bool callbackExecuted = false;
        navService.RegionCacheCleared += region =>
        {
            // If events were raised while holding _navigationLock, calling CanGoBack or other methods could lock
            callbackExecuted = true;
            bool canGoBack = navService.CanGoBack(region);
            Assert.False(canGoBack);
        };

        await navService.DisposeAsync();
        Assert.True(callbackExecuted);
        Assert.Equal(1, keepAlive.DisposeCallCount);
    }

    [Fact]
    public async Task ClearAllCache_WhenStackIsEmpty_ShouldStillNotifyCachedRegion()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>("EmptyStackRegion");
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("EmptyStackRegion", NavigationMode.KeepAlive);

        // Pop back to home, then clear stack
        await _navigationService.GoBackAsync("EmptyStackRegion");
        await _navigationService.NavigateToAsync<TestHomeViewModel>("EmptyStackRegion", NavigationMode.ClearStack);

        var notifiedRegions = new List<string>();
        _navigationService.RegionCacheCleared += r => notifiedRegions.Add(r);

        _navigationService.ClearAllCache();

        Assert.Contains("EmptyStackRegion", notifiedRegions);
    }

    // ────────────────── Disposal-failure isolation (P1 regression) ──────────────────

    /// <summary>
    /// Puts a KeepAlive ViewModel into the cache and then off the navigation stack,
    /// so a later cache clear disposes it directly.
    /// </summary>
    private async Task<ThrowingKeepAliveViewModel> CacheThrowingViewModelAsync(string regionName)
    {
        await _navigationService.NavigateToAsync<ThrowingKeepAliveViewModel>(regionName, NavigationMode.KeepAlive);
        var vm = (ThrowingKeepAliveViewModel)_navigationService.GetCurrentViewModel(regionName)!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>(regionName, NavigationMode.Replace);
        return vm;
    }

    private async Task<KeepAliveViewModel> CacheHealthyViewModelAsync(string regionName)
    {
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(regionName, NavigationMode.KeepAlive);
        var vm = (KeepAliveViewModel)_navigationService.GetCurrentViewModel(regionName)!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>(regionName, NavigationMode.Replace);
        return vm;
    }

    [Fact]
    public async Task NavigateAway_WhenPageScopeDisposalThrows_ViewModelReleasedStillFires_PinOwnershipReleased()
    {
        INavigationViewModel? releasedVm = null;
        string? releasedRegion = null;
        _navigationService.ViewModelReleased += (region, vm) => { releasedRegion = region; releasedVm = vm; };

        await _navigationService.NavigateToAsync<ThrowingKeepAliveViewModel>("EventRegion");
        var throwing = (ThrowingKeepAliveViewModel)_navigationService.GetCurrentViewModel("EventRegion")!;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<TestHomeViewModel>("EventRegion", NavigationMode.Replace));
        Assert.Equal("Sync dispose failed.", ex.Message);

        // Ownership was released even though the scope disposal threw; the error still
        // propagates to the caller, and navigation itself completed.
        Assert.Same(throwing, releasedVm);
        Assert.Equal("EventRegion", releasedRegion);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>("EventRegion"));
    }

    [Fact]
    public async Task ClearCacheAsync_WhenOneViewModelThrows_ShouldStillDisposeRemaining()
    {
        var throwing = await CacheThrowingViewModelAsync("CacheRegion");
        var healthy = await CacheHealthyViewModelAsync("CacheRegion");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.ClearCacheAsync("CacheRegion").AsTask());

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(healthy.Disposed);
    }

    [Fact]
    public async Task ClearCache_WhenOneViewModelThrows_ShouldStillDisposeRemaining()
    {
        var throwing = await CacheThrowingViewModelAsync("SyncCacheRegion");
        var healthy = await CacheHealthyViewModelAsync("SyncCacheRegion");

        // Exercise the synchronous clear path; only the setup above is asynchronous.
        var ex = Assert.Throws<InvalidOperationException>(() => _navigationService.ClearCache("SyncCacheRegion"));

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(healthy.Disposed);
    }

    [Fact]
    public async Task ClearAllCacheAsync_WhenMultipleViewModelsThrow_ShouldAggregateExceptions()
    {
        var throwingA = await CacheThrowingViewModelAsync("RegionA");
        var throwingB = await CacheThrowingViewModelAsync("RegionB");

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => _navigationService.ClearAllCacheAsync().AsTask());

        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.True(throwingA.DisposeAttempted);
        Assert.True(throwingB.DisposeAttempted);
    }

    [Fact]
    public async Task DisposeAsync_WhenCacheViewModelThrows_ShouldStillDisposeStackViewModels()
    {
        var throwing = await CacheThrowingViewModelAsync("DisposeRegion");

        // A healthy KeepAlive ViewModel that stays on the navigation stack.
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("DisposeRegion", NavigationMode.KeepAlive);
        var stacked = (KeepAliveViewModel)_navigationService.GetCurrentViewModel("DisposeRegion")!;

        var navService = (NavigationService)_navigationService;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => navService.DisposeAsync().AsTask());

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(stacked.Disposed);
    }

    [Fact]
    public async Task Dispose_WhenStackViewModelThrows_ShouldStillDisposeRemainingStackViewModels()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ThrowingKeepAliveViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var sp = services.BuildServiceProvider();
        var navService = (NavigationService)sp.GetRequiredService<INavigationService>();

        await navService.NavigateToAsync<ThrowingKeepAliveViewModel>("StackRegion");
        var throwing = (ThrowingKeepAliveViewModel)navService.GetCurrentViewModel("StackRegion")!;
        await navService.NavigateToAsync<KeepAliveViewModel>("StackRegion");
        var stacked = (KeepAliveViewModel)navService.GetCurrentViewModel("StackRegion")!;

        var ex = Assert.Throws<InvalidOperationException>(() => navService.Dispose());

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(stacked.Disposed);
    }
    #region Page-level service scope (v2.0)

    [Fact]
    public async Task NavigateToAsync_WhenNavigatingToSameViewModel_ShouldReturnTrue_WithoutPushing()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.True(result);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task SingletonViewModel_WhenNavigatedAway_ShouldNeverBeDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<SingletonViewModel>();
        var singleton = (SingletonViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.False(singleton.Disposed);
        Assert.True(await navigationService.GoBackAsync());
        Assert.Same(singleton, navigationService.GetCurrentViewModel());
        Assert.False(singleton.Disposed);

        // Root-owned: neither a page scope nor the service may dispose it.
        await navigationService.DisposeAsync();
        Assert.False(singleton.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task NavigateToAsync_WhenSingletonDeeperOnStack_PopsBackToExistingInstance()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var inner = services.BuildServiceProvider();
        var tracking = new TrackingScopeFactory(inner.GetRequiredService<IServiceScopeFactory>());
        var navigationService = new NavigationService(new TestServiceProvider(inner, tracking));

        await navigationService.NavigateToAsync<SingletonViewModel>();
        var singleton = (SingletonViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<TestHomeViewModel>();

        var result = await navigationService.NavigateToAsync<SingletonViewModel>();

        // Mainstream behavior: pop back to the shared instance instead of fail-fast.
        Assert.True(result);
        Assert.Same(singleton, navigationService.GetCurrentViewModel());
        Assert.False(navigationService.CanGoBack());
        Assert.False(singleton.Disposed);

        // Three scopes were created (singleton page, home page, abandoned re-resolution);
        // the abandoned one and the popped home page's scope were released: no leak.
        Assert.Equal(3, tracking.CreatedScopes);
        Assert.Equal(2, tracking.DisposedScopes);

        await navigationService.DisposeAsync();
        Assert.Equal(3, tracking.DisposedScopes);
    }

    [Fact]
    public async Task NavigateToAsync_WhenTargetIsAlreadyActive_ShouldBeNoOp_WithoutResolvingDuplicate()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        var inner = services.BuildServiceProvider();
        var tracking = new TrackingScopeFactory(inner.GetRequiredService<IServiceScopeFactory>());
        var navigationService = new NavigationService(new TestServiceProvider(inner, tracking));

        Assert.True(await navigationService.NavigateToAsync<TestHomeViewModel>());
        var first = navigationService.GetCurrentViewModel();
        Assert.True(await navigationService.NavigateToAsync<TestHomeViewModel>());
        var second = navigationService.GetCurrentViewModel();

        Assert.Same(first, second);
        Assert.False(navigationService.CanGoBack());
        // One scope for the page; the no-op creates no scope at all (no duplicate
        // ViewModel is resolved and discarded).
        Assert.Equal(1, tracking.CreatedScopes);
        Assert.Equal(0, tracking.DisposedScopes);

        await navigationService.DisposeAsync();
        Assert.Equal(1, tracking.DisposedScopes);
    }

    [Fact]
    public async Task TransientDependencyGraph_ShouldBeDisposedWhenPageIsPopped()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<DisposableService>();
        services.AddTransient<ServiceDependentViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<ServiceDependentViewModel>();
        var viewModel = (ServiceDependentViewModel)navigationService.GetCurrentViewModel()!;
        Assert.False(viewModel.Service.Disposed);

        await navigationService.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.Replace);

        // The page scope owns the whole dependency graph, so the injected service
        // is released together with the page's ViewModel.
        Assert.True(viewModel.Disposed);
        Assert.True(viewModel.Service.Disposed);

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ScopedViewModel_ShouldHaveOneInstancePerPage_WithIndependentDisposal()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddScoped<ScopedViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<ScopedViewModel>();
        var first = (ScopedViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<TestHomeViewModel>();
        await navigationService.NavigateToAsync<ScopedViewModel>();
        var second = (ScopedViewModel)navigationService.GetCurrentViewModel()!;

        // Each page gets its own scope, so each Scoped registration resolves once per page.
        Assert.NotEqual(first.InstanceId, second.InstanceId);

        Assert.True(await navigationService.GoBackAsync());
        Assert.True(second.Disposed);
        Assert.False(first.Disposed);
        Assert.True(navigationService.IsActive<TestHomeViewModel>());

        Assert.True(await navigationService.GoBackAsync());
        Assert.Same(first, navigationService.GetCurrentViewModel());

        await navigationService.DisposeAsync();
        Assert.True(first.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task PageScopeDisposal_WhenViewModelThrows_ShouldAbortThatScope_ButIsolateAcrossPages()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<DisposableService>();
        services.AddTransient<ThrowingDependencyViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<ThrowingDependencyViewModel>();
        var throwing = (ThrowingDependencyViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<KeepAliveViewModel>();
        var healthy = (KeepAliveViewModel)navigationService.GetCurrentViewModel()!;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            navigationService.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.ClearStack));

        Assert.Equal("VM dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        // Pinned platform behavior: IServiceScope.Dispose stops at the first throwing
        // disposable, so the dependency created before the ViewModel is never released.
        Assert.False(throwing.Service.Disposed);
        // Cross-page isolation: the healthy page's scope was still disposed.
        Assert.True(healthy.Disposed);

        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ClearCache_WithSingletonKeepAlive_ShouldNotDisposeSingleton_AndAllowRenavigation()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<KeepAliveViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        var first = (KeepAliveViewModel)navigationService.GetCurrentViewModel()!;

        await navigationService.ClearCacheAsync("MainRegion");
        Assert.False(first.Disposed);

        // Renavigation creates a fresh page scope around the same singleton instance.
        await navigationService.NavigateToAsync<KeepAliveViewModel>(mode: NavigationMode.KeepAlive);
        Assert.Same(first, navigationService.GetCurrentViewModel());
        Assert.False(first.Disposed);

        await navigationService.DisposeAsync();
        Assert.False(first.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ClearCache_Sync_WithAsyncOnlyViewModel_ShouldThrowInvalidOperationException_PinPlatformBehavior()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestDetailViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<TestHomeViewModel>();
        await navigationService.NavigateToAsync<TestDetailViewModel, string>("Arg", "MainRegion", NavigationMode.KeepAlive);
        await navigationService.GoBackAsync();

        // TestDetailViewModel only implements IAsyncDisposable and now sits in the
        // KeepAlive cache. The DI container refuses synchronous disposal of such
        // scopes; the platform exception surfaces as-is so the caller knows to use
        // the asynchronous APIs.
        var ex = Assert.Throws<InvalidOperationException>(() => navigationService.ClearCache("MainRegion"));
        Assert.Contains("IAsyncDisposable", ex.Message);

        // The asynchronous path handles the same page cleanly.
        await navigationService.ClearCacheAsync("MainRegion");

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentNavigationsAndClearCache_ShouldNotLeakScopes_OrThrow()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        var inner = services.BuildServiceProvider();
        var tracking = new TrackingScopeFactory(inner.GetRequiredService<IServiceScopeFactory>());
        var navigationService = new NavigationService(new TestServiceProvider(inner, tracking));

        var tasks = new List<Task>();
        for (int worker = 0; worker < 8; worker++)
        {
            int capturedWorker = worker;
            tasks.Add(Task.Run(async () =>
            {
                string region = capturedWorker % 2 == 0 ? "R1" : "R2";
                for (int i = 0; i < 50; i++)
                {
                    switch ((i + capturedWorker) % 4)
                    {
                        case 0:
                            await navigationService.NavigateToAsync<TestHomeViewModel>(region);
                            break;
                        case 1:
                            await navigationService.NavigateToAsync<TestDetailViewModel>(region);
                            break;
                        case 2:
                            await navigationService.NavigateToAsync<KeepAliveViewModel>(region, NavigationMode.KeepAlive);
                            break;
                        default:
                            await navigationService.ClearCacheAsync(region);
                            break;
                    }

                    await navigationService.GoBackAsync(region);
                }
            }));
        }

        await Task.WhenAll(tasks);
        await navigationService.DisposeAsync();

        // Every page scope ever created was released exactly once: nothing leaked,
        // even with ClearCache racing navigation (stale cache entries are re-validated).
        Assert.Equal(tracking.CreatedScopes, tracking.DisposedScopes);
        Assert.True(tracking.CreatedScopes > 0);
    }

    #endregion

    #region Review feedback: cancellation, async lifecycle, refresh, guard context, thin base

    [Fact]
    public async Task NavigateToAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _navigationService.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token));

        Assert.Null(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task NavigateToAsync_WhenCancelledDuringResolve_ReleasesAbandonedScopeAndThrows()
    {
        TrackedDisposeViewModel.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TrackedDisposeViewModel>();
        var innerProvider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource();
        var provider = new TestServiceProvider(
            innerProvider,
            new CancellingScopeFactory(innerProvider.GetRequiredService<IServiceScopeFactory>(), cts));
        var navigationService = new NavigationService(provider);

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => navigationService.NavigateToAsync<TrackedDisposeViewModel>(cancellationToken: cts.Token));

        Assert.Equal(cts.Token, ex.CancellationToken);
        // The scope created for the abandoned page was released: nothing leaked.
        Assert.Equal(1, TrackedDisposeViewModel.DisposeCount);

        await navigationService.DisposeAsync();
        await innerProvider.DisposeAsync();
    }

    [Fact]
    public async Task NavigateToAsync_WhenCancelledAfterStackUpdate_RunsToCompletion()
    {
        using var cts = new CancellationTokenSource();
        CancelInNavigatedToViewModel.TokenSource = cts;
        try
        {
            await _navigationService.NavigateToAsync<TestHomeViewModel>();
            var result = await _navigationService.NavigateToAsync<CancelInNavigatedToViewModel>(
                cancellationToken: cts.Token);

            Assert.True(result);
            Assert.True(_navigationService.IsActive<CancelInNavigatedToViewModel>());
        }
        finally
        {
            CancelInNavigatedToViewModel.TokenSource = null;
        }
    }

    [Fact]
    public async Task GoBackAsync_WhenTokenAlreadyCancelled_ThrowsAndKeepsStack()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("x");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _navigationService.GoBackAsync(cancellationToken: cts.Token));

        Assert.True(_navigationService.IsActive<TestDetailViewModel>());
    }

    [Fact]
    public async Task INavigationAwareAsync_TakesPrecedenceOverSyncCallbacks()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<BothAwareViewModel>();
        var vm = (BothAwareViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.True(vm.AsyncToCalled);
        Assert.False(vm.SyncToCalled);

        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("x");

        Assert.True(vm.AsyncFromCalled);
        Assert.False(vm.SyncFromCalled);
    }

    [Fact]
    public async Task INavigationAwareAsync_WhenCallbackThrows_ExceptionPropagatesAfterTransition()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<AsyncThrowingViewModel>());

        Assert.Equal("OnNavigatedToAsync failed.", ex.Message);
        // The page was committed before the callback ran.
        Assert.True(_navigationService.IsActive<AsyncThrowingViewModel>());
    }

    [Fact]
    public async Task INavigationAwareAsync_Typed_ReceivesStronglyTypedParameter()
    {
        await _navigationService.NavigateToAsync<TypedAsyncViewModel, string>("typed-param");
        var vm = (TypedAsyncViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.Equal("typed-param", vm.ReceivedParam);
    }

    [Fact]
    public async Task NavigateToAsync_RefreshIfActive_ReinvokesCallbacksOnSameInstance()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        int navigatedEvents = 0;
        _navigationService.RegionNavigated += (_, _) => navigatedEvents++;

        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "second", refreshIfActive: true);

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, vm.NavigatedToCount);
        Assert.Equal("second", vm.LastParameter);
        Assert.Equal(1, navigatedEvents);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_SameTypeWithoutRefresh_IsSilentNoOp()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        int navigatedEvents = 0;
        _navigationService.RegionNavigated += (_, _) => navigatedEvents++;

        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>("second");

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(1, vm.NavigatedToCount);
        Assert.Equal(0, navigatedEvents);
    }

    [Fact]
    public async Task NavigateToAsync_RefreshIfActive_WhenNotOnTop_NavigatesNormally()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "p", refreshIfActive: true);

        Assert.True(result);
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Equal(1, vm.NavigatedToCount);
        Assert.Equal("p", vm.LastParameter);
        Assert.True(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_Refresh_PersistsNewParameterForGoBack()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("second", refreshIfActive: true);
        Assert.Equal("second", vm.LastParameter);

        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("detail");
        Assert.True(_navigationService.CanGoBack());

        await _navigationService.GoBackAsync();

        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        // The refreshed parameter was persisted on the stack entry, so GoBack
        // re-activates with "second", not the original "first".
        Assert.Equal("second", vm.LastParameter);
    }

    [Fact]
    public async Task NavigateToAsync_Refresh_DoesNotResolveTemporaryViewModel()
    {
        ConstructionCountingViewModel.ConstructionCount = 0;

        await _navigationService.NavigateToAsync<ConstructionCountingViewModel>();
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        var vm = (ConstructionCountingViewModel)_navigationService.GetCurrentViewModel()!;

        // Refresh must reuse the active instance without creating (and discarding)
        // a temporary ViewModel: no extra construction, no extra disposal.
        var result = await _navigationService.NavigateToAsync<ConstructionCountingViewModel>(
            refreshIfActive: true);

        Assert.True(result);
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, vm.NavigatedToCount);
    }

    [Fact]
    public async Task NavigateToAsync_SameTypeNoOp_DoesNotResolveTemporaryViewModel()
    {
        ConstructionCountingViewModel.ConstructionCount = 0;

        await _navigationService.NavigateToAsync<ConstructionCountingViewModel>();
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        var vm = (ConstructionCountingViewModel)_navigationService.GetCurrentViewModel()!;

        // Silent no-op: same instance, no callbacks, and no temporary resolution.
        var result = await _navigationService.NavigateToAsync<ConstructionCountingViewModel>();

        Assert.True(result);
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(1, vm.NavigatedToCount);
    }

    [Fact]
    public async Task NavigateToAsync_Replace_ToActiveType_ReplacesEntry()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var first = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        // Explicit Replace takes precedence over the same-type no-op.
        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "second", mode: NavigationMode.Replace);

        Assert.True(result);
        var second = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.Equal("second", second.LastParameter);
        Assert.Equal(1, second.NavigatedToCount);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_ClearStack_ToActiveType_ClearsStackAndPushesFresh()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var first = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(_navigationService.CanGoBack());

        // Explicit ClearStack takes precedence over the same-type no-op.
        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "second", mode: NavigationMode.ClearStack);

        Assert.True(result);
        var second = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.Equal("second", second.LastParameter);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_KeepAlive_ToActiveType_ReactivatesWithNewParameter()
    {
        await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", mode: NavigationMode.KeepAlive);
        var vm = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Equal("first", vm.LastParameter);

        // Explicit KeepAlive takes precedence over the same-type no-op: the
        // cached (active) instance is re-activated with the new parameter.
        var result = await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "second", mode: NavigationMode.KeepAlive);

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal("second", vm.LastParameter);
        Assert.False(vm.Disposed);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task INavigationGuardWithContext_ReceivesFullContext()
    {
        await _navigationService.NavigateToAsync<ContextGuardViewModel>();
        var guard = (ContextGuardViewModel)_navigationService.GetCurrentViewModel()!;

        var result = await _navigationService.NavigateToAsync<TestDetailViewModel, string>("p1");

        Assert.True(result);
        var ctx = guard.LastContext!;
        Assert.Equal("MainRegion", ctx.RegionName);
        Assert.Equal(typeof(TestDetailViewModel), ctx.TargetViewModelType);
        Assert.Equal(NavigationMode.New, ctx.Mode);
        Assert.Equal("p1", ctx.Parameter);
        Assert.False(ctx.IsBack);
    }

    [Fact]
    public async Task INavigationGuardWithContext_Deny_CancelsNavigation()
    {
        await _navigationService.NavigateToAsync<ContextGuardViewModel>();
        var guard = (ContextGuardViewModel)_navigationService.GetCurrentViewModel()!;
        guard.Allow = false;

        var result = await _navigationService.NavigateToAsync<TestDetailViewModel>();

        Assert.False(result);
        Assert.Same(guard, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task INavigationGuardWithContext_TakesPrecedenceOverPlainGuard()
    {
        await _navigationService.NavigateToAsync<BothGuardViewModel>();
        var guard = (BothGuardViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>());

        Assert.True(guard.ContextGuardCalled);
        Assert.False(guard.PlainGuardCalled);
    }

    [Fact]
    public async Task GoBack_GuardWithContext_ReceivesBackContext()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel, string>("home-param");
        await _navigationService.NavigateToAsync<ContextGuardDetailViewModel>();
        var guard = (ContextGuardDetailViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.True(await _navigationService.GoBackAsync());

        var ctx = guard.LastContext!;
        Assert.True(ctx.IsBack);
        Assert.Null(ctx.Mode);
        Assert.Equal(typeof(TestHomeViewModel), ctx.TargetViewModelType);
        Assert.Equal("home-param", ctx.Parameter);
        Assert.Equal("MainRegion", ctx.RegionName);
    }

    [Fact]
    public async Task NavigationViewModelBase_ThinBase_WorksWithoutToolkit()
    {
        await _navigationService.NavigateToAsync<ThinViewModel>();
        var thin = (ThinViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.NotNull(thin.Navigation);

        int changed = 0;
        thin.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThinViewModel.Title))
            {
                changed++;
            }
        };
        thin.Title = "hello";
        thin.Title = "hello"; // unchanged value: no event

        Assert.Equal(1, changed);

        Assert.True(await thin.GoToDetailAsync());
        Assert.True(_navigationService.IsActive<TestDetailViewModel>());
        Assert.Null(thin.Navigation);
    }

    [Fact]
    public async Task ViewModelReleased_FiresForSingletonPageTeardown_ButInstanceSurvives()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        INavigationViewModel? disposedVm = null;
        navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await navigationService.NavigateToAsync<SingletonViewModel>();
        var singleton = (SingletonViewModel)navigationService.GetCurrentViewModel()!;
        // Replace destroys the singleton's page (a merely covered page in New mode stays
        // alive on the back stack and must NOT raise ViewModelReleased).
        await navigationService.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.Replace);

        // Ownership was released (event fired) but the root-owned instance survives.
        Assert.Same(singleton, disposedVm);
        Assert.False(singleton.Disposed);

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    #endregion

    #region v2.0 test doubles

    private sealed class RefreshableViewModel : NavigationViewModelBase, INavigationAware
    {
        public int NavigatedToCount { get; private set; }
        public object? LastParameter { get; private set; }

        public void OnNavigatedTo(object? parameter)
        {
            NavigatedToCount++;
            LastParameter = parameter;
        }

        public void OnNavigatedFrom() { }
    }

    private sealed class ConstructionCountingViewModel : NavigationViewModelBase, INavigationAware
    {
        public static int ConstructionCount;

        public ConstructionCountingViewModel()
        {
            Interlocked.Increment(ref ConstructionCount);
        }

        public int NavigatedToCount { get; private set; }

        public void OnNavigatedTo(object? parameter) => NavigatedToCount++;

        public void OnNavigatedFrom() { }
    }

    private sealed class BothAwareViewModel : NavigationViewModelBase, INavigationAware, INavigationAwareAsync
    {
        public bool SyncToCalled { get; private set; }
        public bool SyncFromCalled { get; private set; }
        public bool AsyncToCalled { get; private set; }
        public bool AsyncFromCalled { get; private set; }

        public void OnNavigatedTo(object? parameter) => SyncToCalled = true;
        public void OnNavigatedFrom() => SyncFromCalled = true;

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            AsyncToCalled = true;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            AsyncFromCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class SlowInitViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public TaskCompletionSource<bool> InitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> InitCanComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ReceivedToken { get; private set; }
        public bool TokenWasCancelled { get; private set; }
        /// <summary>Simulates slow cleanup after observing cancellation.</summary>
        public bool SimulateSlowCleanup { get; set; }

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            ReceivedToken = cancellationToken;
            InitStarted.TrySetResult(true);
            try
            {
                await InitCanComplete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TokenWasCancelled = true;
                if (SimulateSlowCleanup)
                {
                    // Non-cancellable cleanup: disposal gating must wait for this.
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                throw;
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TokenCapturingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public CancellationToken LastToken { get; private set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SelfReplacingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Navigation is not null)
            {
                await Navigation.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.Replace).ConfigureAwait(false);
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RefreshTrackingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public CancellationToken LastToken { get; private set; }
        public TaskCompletionSource? BlockActivation { get; set; }

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            if (BlockActivation is not null)
            {
                var gate = BlockActivation;
                BlockActivation = null; // Only block once.
                await gate.Task.ConfigureAwait(false);
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CancellableViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public CancellationToken Token { get; private set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DisposableTracker : IDisposable
    {
        private bool _disposed;
        public bool UsedAfterReplace { get; private set; }
        public void MarkUsed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DisposableTracker));
            UsedAfterReplace = true;
        }
        public void Dispose() => _disposed = true;
    }

    private sealed class SelfReplacingAndContinuingViewModel(DisposableTracker tracker) : NavigationViewModelBase, INavigationAwareAsync
    {
        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Navigation is not null)
            {
                // Reentrant self-destroying navigation...
                await Navigation.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.Replace).ConfigureAwait(false);
                // ...then continue using scope services. Throws if scope was disposed.
                tracker.MarkUsed();
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class SlowLeaveViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public TaskCompletionSource? LeaveGate { get; set; }
        public bool LeaveCompleted { get; private set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            if (LeaveGate is not null)
            {
                await LeaveGate.Task.ConfigureAwait(false);
            }
            LeaveCompleted = true;
        }
    }

    private sealed class ReentrantAsyncViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Navigation is not null)
            {
                await Navigation.NavigateToAsync<TestHomeViewModel>().ConfigureAwait(false);
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HangingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public TaskCompletionSource<bool> InitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            InitStarted.TrySetResult(true);
            // Ignores cancellation entirely: hangs forever.
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AsyncThrowingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("OnNavigatedToAsync failed."));

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TypedAsyncViewModel : NavigationViewModelBase, INavigationAwareAsync<string>
    {
        public string? ReceivedParam { get; private set; }

        public Task OnNavigatedToAsync(string parameter, CancellationToken cancellationToken)
        {
            ReceivedParam = parameter;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ContextGuardViewModel : NavigationViewModelBase, INavigationGuardWithContext
    {
        public NavigationGuardContext? LastContext { get; private set; }
        public bool Allow { get; set; } = true;

        public Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(Allow);
        }
    }

    private sealed class BothGuardViewModel : NavigationViewModelBase, INavigationGuard, INavigationGuardWithContext
    {
        public bool PlainGuardCalled { get; private set; }
        public bool ContextGuardCalled { get; private set; }

        public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            PlainGuardCalled = true;
            return Task.FromResult(true);
        }

        public Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken)
        {
            ContextGuardCalled = true;
            return Task.FromResult(true);
        }
    }

    private sealed class ContextGuardDetailViewModel : NavigationViewModelBase, INavigationGuardWithContext
    {
        public NavigationGuardContext? LastContext { get; private set; }

        public Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(true);
        }
    }

    private sealed class CancelInNavigatedToViewModel : NavigationViewModelBase, INavigationAware
    {
        public static CancellationTokenSource? TokenSource;

        public void OnNavigatedTo(object? parameter) => TokenSource?.Cancel();
        public void OnNavigatedFrom() { }
    }

    private sealed class ThinViewModel : NavigationViewModelBase
    {
        private string? _title;

        public string? Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public Task<bool> GoToDetailAsync() => NavigateToAsync<TestDetailViewModel>();
    }

    /// <summary>
    /// Cancels the token on the first created scope, simulating cancellation that lands
    /// between page-scope creation and the stack update.
    /// </summary>
    private sealed class CancellingScopeFactory(IServiceScopeFactory inner, CancellationTokenSource cts) : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner = inner;
        private readonly CancellationTokenSource _cts = cts;
        private int _calls;

        public IServiceScope CreateScope()
        {
            var scope = _inner.CreateScope();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _cts.Cancel();
            }

            return scope;
        }
    }

    private sealed class SingletonViewModel : NavigationViewModelBase, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class DisposableService : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ServiceDependentViewModel : NavigationViewModelBase, IDisposable
    {
        public DisposableService Service { get; }
        public bool Disposed { get; private set; }

        public ServiceDependentViewModel(DisposableService service)
        {
            Service = service;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class ScopedViewModel : NavigationViewModelBase, IDisposable
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingDependencyViewModel : NavigationViewModelBase, IDisposable
    {
        public DisposableService Service { get; }
        public bool DisposeAttempted { get; private set; }

        public ThrowingDependencyViewModel(DisposableService service)
        {
            Service = service;
        }

        public void Dispose()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("VM dispose failed.");
        }
    }

    /// <summary>
    /// Wraps an <see cref="IServiceScopeFactory"/> and counts created and disposed page scopes.
    /// </summary>
    private sealed class TrackingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner = inner;
        public int CreatedScopes;
        public int DisposedScopes;

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref CreatedScopes);
            return new TrackingScope(_inner.CreateScope(), this);
        }

        private sealed class TrackingScope(IServiceScope inner, TrackingScopeFactory factory) : IServiceScope, IAsyncDisposable
        {
            private readonly IServiceScope _inner = inner;
            private readonly TrackingScopeFactory _factory = factory;

            public IServiceProvider ServiceProvider => _inner.ServiceProvider;

            public void Dispose()
            {
                _inner.Dispose();
                Interlocked.Increment(ref _factory.DisposedScopes);
            }

            public async ValueTask DisposeAsync()
            {
                if (_inner is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    _inner.Dispose();
                }

                Interlocked.Increment(ref _factory.DisposedScopes);
            }
        }
    }

    /// <summary>
    /// Forwards <see cref="IServiceProvider"/> lookups to an inner provider, but substitutes
    /// a custom <see cref="IServiceScopeFactory"/>.
    /// </summary>
    private sealed class TestServiceProvider(IServiceProvider inner, IServiceScopeFactory scopeFactory) : IServiceProvider
    {
        private readonly IServiceProvider _inner = inner;
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? _scopeFactory : _inner.GetService(serviceType);
    }

    #endregion

    #region Review Regression Tests (P1/P2 fixes)

    [Fact]
    public async Task ClearStack_ShouldNotRepeatNavigatedFrom()
    {
        // A -> B -> C, then ClearStack to D.
        // A already received OnNavigatedFrom when B covered it; ClearStack must not
        // notify it again. Only C (the active page) gets OnNavigatedFrom here.
        // Note: A and C share the type but are different instances (B is a different
        // type in between, so no same-type no-op applies).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<CountingAwareViewModel>();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<CountingAwareViewModel>(); // A (instance 1)
        var a = (CountingAwareViewModel)nav.GetCurrentViewModel()!;
        await nav.NavigateToAsync<TestHomeViewModel>(); // B covers A
        await nav.NavigateToAsync<CountingAwareViewModel>(); // C (instance 2) covers B
        var c = (CountingAwareViewModel)nav.GetCurrentViewModel()!;
        Assert.NotSame(a, c);

        Assert.Equal(1, a.NavigatedFromCount); // covered by B
        Assert.Equal(0, c.NavigatedFromCount); // active

        await nav.NavigateToAsync<TestDetailViewModel>(mode: NavigationMode.ClearStack);

        Assert.Equal(1, a.NavigatedFromCount); // must NOT be notified again
        Assert.Equal(1, c.NavigatedFromCount); // active page: exactly once
    }

    [Fact]
    public async Task PopToExisting_ShouldNotRepeatNavigatedFrom()
    {
        // A(KeepAlive) -> B -> back to A via KeepAlive (pop-to-existing).
        // B was active when popped, so it gets OnNavigatedFrom once.
        // A was already inactive (covered by B); popping B must not notify A again.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<CountingAwareViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<CountingAwareViewModel>(mode: NavigationMode.KeepAlive); // A
        var a = (CountingAwareViewModel)nav.GetCurrentViewModel()!;
        await nav.NavigateToAsync<TestHomeViewModel>(); // B covers A
        Assert.Equal(1, a.NavigatedFromCount);

        await nav.NavigateToAsync<CountingAwareViewModel>(mode: NavigationMode.KeepAlive); // pop back to A
        Assert.Same(a, nav.GetCurrentViewModel());
        Assert.Equal(1, a.NavigatedFromCount); // must NOT be notified again
        Assert.Equal(2, a.NavigatedToCount);   // reactivated
    }

    [Fact]
    public async Task ReentrantNavigation_ShouldSuppressStaleRegionNavigatedEvent()
    {
        // ReentrantNavigatedToViewModel navigates to TestHomeViewModel inside OnNavigatedTo.
        // The outer transition's RegionNavigated(Reentrant) event must be suppressed;
        // only RegionNavigated(Home) should fire, and the final page must be Home.
        var events = new List<string>();
        _navigationService.RegionNavigated += (_, vm) => events.Add(vm.GetType().Name);

        await _navigationService.NavigateToAsync<ReentrantNavigatedToViewModel>();

        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        Assert.DoesNotContain(nameof(ReentrantNavigatedToViewModel), events);
        Assert.Equal(new[] { nameof(TestHomeViewModel) }, events);
    }

    [Fact]
    public async Task SameTypeNoOp_ShouldNotBeVetoedByGuard()
    {
        // Navigating to the already-active type (New mode, no refresh) is a no-op
        // and must not be vetoed by the leave guard.
        await _navigationService.NavigateToAsync<GuardedViewModel>();
        var guarded = (GuardedViewModel)_navigationService.GetCurrentViewModel()!;
        guarded.AllowNavigation = false;

        var result = await _navigationService.NavigateToAsync<GuardedViewModel>();

        Assert.True(result);
        Assert.Same(guarded, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task SubscriberError_ShouldBeReportedWithoutBreakingNavigation()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += ex => errors.Add(ex);
        _navigationService.RegionNavigated += (_, _) => throw new InvalidOperationException("boom");

        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.True(result);
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task ClearStack_ToSingletonAlreadyOnStack_ShouldActuallyClear()
    {
        // Home -> Settings(singleton) -> Detail, then ClearStack to Settings.
        // The resolved Settings instance is the same object already on the stack;
        // ClearStack must still empty the stack instead of popping back to it.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SingletonViewModel>();
        var settings = nav.GetCurrentViewModel();
        await nav.NavigateToAsync<TestDetailViewModel>();
        Assert.True(nav.CanGoBack());

        var result = await nav.NavigateToAsync<SingletonViewModel>(mode: NavigationMode.ClearStack);

        Assert.True(result);
        Assert.Same(settings, nav.GetCurrentViewModel());
        Assert.False(nav.CanGoBack()); // stack holds only Settings
    }

    [Fact]
    public async Task Replace_ToSingletonAlreadyOnStack_ShouldNotDuplicate()
    {
        // Home -> Settings(singleton) -> Detail, then Replace top with Settings.
        // The deeper Settings entry must be removed so the singleton appears once.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SingletonViewModel>();
        var settings = nav.GetCurrentViewModel();
        await nav.NavigateToAsync<TestDetailViewModel>();

        var result = await nav.NavigateToAsync<SingletonViewModel>(mode: NavigationMode.Replace);

        Assert.True(result);
        Assert.Same(settings, nav.GetCurrentViewModel());
        // Home -> Settings: back once to Home, then nothing.
        Assert.True(nav.CanGoBack());
        Assert.True(await nav.GoBackAsync());
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        Assert.False(nav.CanGoBack());
    }

    [Fact]
    public async Task SlowInit_CancelledOnNavigateAway()
    {
        // B's OnNavigatedToAsync is still running when we navigate away: its token
        // must be cancelled so it stops touching the page's dependencies.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var navTask = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Navigate away while init is still in flight.
        var awayTask = nav.NavigateToAsync<TestHomeViewModel>();
        await awayTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Let the cancelled init observe the token.
        slowVm.InitCanComplete.TrySetResult(true);
        await navTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(slowVm.TokenWasCancelled);
        Assert.True(slowVm.ReceivedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Replace_WaitsForInFlightLifecycleBeforeDisposing()
    {
        // Replace B with C while B's OnNavigatedToAsync is in flight: the scope must
        // not be released until B's callback completes (disposal gating).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var navTask = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        slowVm.SimulateSlowCleanup = true;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var replaceTask = nav.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.Replace);
        // Give the replace a moment to reach the disposal gate (it should block
        // while B's slow cleanup runs).
        await Task.Delay(300);
        Assert.False(replaceTask.IsCompleted); // gated: waiting for B's cleanup

        await replaceTask.WaitAsync(TimeSpan.FromSeconds(10));
        await navTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        Assert.True(slowVm.TokenWasCancelled);
    }

    [Fact]
    public async Task HangingLifecycle_DisposalTimesOutAndNavigationCompletes()
    {
        // A ViewModel that ignores its cancellation token must not block navigation
        // forever: after the disposal gate timeout, the scope is released anyway.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HangingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var errors = new List<Exception>();
        nav.SubscriberError += ex => errors.Add(ex);

        _ = nav.NavigateToAsync<HangingViewModel>();
        var hangingVm = (HangingViewModel)nav.GetCurrentViewModel()!;
        await hangingVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Replace: disposal gate waits 5s for the hanging init, then times out.
        var replaceTask = nav.NavigateToAsync<TestHomeViewModel>(mode: NavigationMode.Replace);
        await replaceTask.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        var timeoutError = Assert.Single(errors, e => e is TimeoutException);
        Assert.Contains("Disposal gate timed out", timeoutError.Message);
    }

    [Fact]
    public async Task CoveredPage_CancelledOnCover()
    {
        // A covered by B: A's init token must be cancelled when B covers it.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var navTask = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await nav.NavigateToAsync<TestHomeViewModel>().WaitAsync(TimeSpan.FromSeconds(5));

        slowVm.InitCanComplete.TrySetResult(true);
        await navTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(slowVm.TokenWasCancelled);
    }

    [Fact]
    public async Task ReentrantAsyncNavigation_NoDeadlock()
    {
        // Async OnNavigatedTo that re-enters navigation must not deadlock with the
        // disposal gating (no lock is held across the lifecycle).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantAsyncViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<ReentrantAsyncViewModel>().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result);
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task GoBack_ReactivatesWithFreshToken()
    {
        // A -> B -> GoBack: A's OnNavigatedToAsync must receive a NON-cancelled token.
        // (Regression: PR #42 reused the entry whose CTS was cancelled when B covered A.)
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TokenCapturingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TokenCapturingViewModel>();
        var vm = (TokenCapturingViewModel)nav.GetCurrentViewModel()!;
        Assert.False(vm.LastToken.IsCancellationRequested); // first activation

        await nav.NavigateToAsync<TestHomeViewModel>(); // covers A, cancels its CTS
        Assert.True(vm.LastToken.IsCancellationRequested);

        await nav.GoBackAsync(); // reactivates A
        Assert.False(vm.LastToken.IsCancellationRequested); // fresh token!
        Assert.Same(vm, nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task DisposeAsync_WaitsForInFlightLifecycle()
    {
        // DisposeAsync must cancel in-flight callbacks and wait for them (with timeout)
        // before releasing scopes — not dispose out from under a running callback.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        _ = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        slowVm.SimulateSlowCleanup = true;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeTask = ((IAsyncDisposable)nav).DisposeAsync().AsTask();
        await Task.Delay(300);
        Assert.False(disposeTask.IsCompleted); // waiting for slow cleanup

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(slowVm.TokenWasCancelled);
    }

    [Fact]
    public async Task ReentrantReplace_DoesNotSelfWait()
    {
        // A's OnNavigatedToAsync does Replace to B: the disposal gate must not wait
        // for A's own callback (self-wait would burn the 5s timeout).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SelfReplacingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var startTime = DateTime.UtcNow;
        var result = await nav.NavigateToAsync<SelfReplacingViewModel>().WaitAsync(TimeSpan.FromSeconds(10));
        var elapsed = DateTime.UtcNow - startTime;

        Assert.True(result);
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        Assert.True(elapsed < TimeSpan.FromSeconds(4), $"Took {elapsed}, self-wait would take ~5s");
    }

    [Fact]
    public async Task RegionNavigated_ThrowingFirstSubscriber_ShouldNotBlockOthers()
    {
        // Multicast delegate semantics: one throwing subscriber must not prevent
        // later subscribers (e.g. the Host) from receiving the event.
        var errors = new List<Exception>();
        _navigationService.SubscriberError += ex => errors.Add(ex);
        _navigationService.RegionNavigated += (_, _) => throw new InvalidOperationException("first boom");

        string? secondRegion = null;
        INavigationViewModel? secondVm = null;
        _navigationService.RegionNavigated += (r, vm) => { secondRegion = r; secondVm = vm; };

        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.True(result);
        Assert.Equal("MainRegion", secondRegion);
        Assert.IsType<TestHomeViewModel>(secondVm);
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task Refresh_CancelsOldActivationAndReusesEntry()
    {
        // Refresh must not orphan the old activation: the old token is cancelled,
        // the entry (and its in-flight tracking) is reused, and the new activation
        // gets a fresh token.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<RefreshTrackingViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<RefreshTrackingViewModel>();
        Assert.True(result);
        var vm = Assert.IsType<RefreshTrackingViewModel>(nav.GetCurrentViewModel());
        var firstToken = vm.LastToken;
        Assert.False(firstToken.IsCancellationRequested);

        // Block the refresh activation, then verify the old token was cancelled.
        var blockGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.BlockActivation = blockGate;
        var refreshTask = nav.NavigateToAsync<RefreshTrackingViewModel>(refreshIfActive: true);
        await Task.Delay(200); // Let the refresh activation start and block.

        Assert.True(firstToken.IsCancellationRequested, "Refresh must cancel the previous activation token");

        blockGate.SetResult();
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotEqual(firstToken, vm.LastToken);
        Assert.False(vm.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task SyncDispose_CancelsLifecycleTokens()
    {
        // Sync Dispose cannot wait for async callbacks, but it must cancel
        // their tokens before releasing scopes.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<CancellableViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<CancellableViewModel>();
        Assert.True(result);
        var vm = Assert.IsType<CancellableViewModel>(nav.GetCurrentViewModel());

        nav.Dispose();

        Assert.True(vm.Token.IsCancellationRequested, "Sync Dispose must cancel lifecycle tokens");
    }

    [Fact]
    public async Task ReentrantReplace_DefersScopeDisposalUntilCallbackExits()
    {
        // When a callback awaits a self-destroying navigation and then continues
        // using scope services, the scope must not be disposed until the callback
        // exits. DisposableTracker.MarkUsed throws if called after disposal.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SelfReplacingAndContinuingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        services.AddScoped<DisposableTracker>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        // If the scope were disposed immediately on Replace, MarkUsed would throw
        // ObjectDisposedException inside the callback, failing the navigation.
        var result = await nav.NavigateToAsync<SelfReplacingAndContinuingViewModel>().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task ClearCacheAsync_WaitsForInFlightLifecycle()
    {
        // ClearCacheAsync must not release a cached scope while its
        // OnNavigatedFromAsync is still running.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowLeaveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<SlowLeaveViewModel>(mode: NavigationMode.KeepAlive);
        var vm = (SlowLeaveViewModel)nav.GetCurrentViewModel()!;
        vm.LeaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Navigate away (KeepAlive -> cached), leave callback starts and blocks.
        var away = nav.NavigateToAsync<TestHomeViewModel>();
        await Task.Delay(100);
        Assert.False(vm.LeaveCompleted);

        // Clear cache while leave is in-flight: should wait, not dispose early.
        var clear = nav.ClearCacheAsync("MainRegion");
        await Task.Delay(200);
        Assert.False(vm.LeaveCompleted, "ClearCacheAsync must wait for in-flight leave callback");

        vm.LeaveGate.SetResult();
        await away;
        await clear.AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.LeaveCompleted);
    }

    [Fact]
    public async Task Refresh_CancelCallbackMustNotDeadlock()
    {
        // A cancellation callback that reenters the navigation service must not
        // deadlock: Cancel() runs outside the navigation locks.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantCancelViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ReentrantCancelViewModel>();
        var vm = Assert.IsType<ReentrantCancelViewModel>(nav.GetCurrentViewModel());
        vm.EnableReentrantCancel(nav);

        // Refresh triggers BeginNewActivation -> Cancel() outside locks.
        // If Cancel ran while holding the lock, the reentrant ClearCache would deadlock.
        var result = await nav.NavigateToAsync<ReentrantCancelViewModel>(refreshIfActive: true)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        Assert.True(vm.CancelCallbackRan);
    }

    [Fact]
    public async Task ClearCache_CancelCallbackMustNotDeadlock()
    {
        // A KeepAlive entry's cancel callback that reenters ClearCache must not
        // deadlock: ClearCache collects CTSs under the lock and cancels after release.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantCancelViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ReentrantCancelViewModel>(mode: NavigationMode.KeepAlive);
        var vm = Assert.IsType<ReentrantCancelViewModel>(nav.GetCurrentViewModel());
        vm.EnableReentrantCancel(nav);

        // ClearCache cancels the KeepAlive entry's token; the callback reenters ClearCache.
        // If Cancel ran while holding _navigationLock, this would deadlock.
        var clearTask = Task.Run(() => nav.ClearCache("MainRegion"));
        await clearTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.CancelCallbackRan);
    }

    [Fact]
    public async Task Dispose_CancelCallbackMustNotDeadlock()
    {
        // A cancel callback that reenters Dispose must not deadlock: sync Dispose
        // collects CTSs under the lock and cancels after release.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantCancelViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ReentrantCancelViewModel>();
        var vm = Assert.IsType<ReentrantCancelViewModel>(nav.GetCurrentViewModel());
        vm.EnableReentrantDispose(nav);

        // Dispose cancels the entry's token; the callback reenters Dispose.
        // If Cancel ran while holding _navigationLock, this would deadlock.
        var disposeTask = Task.Run(() => ((IDisposable)nav).Dispose());
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.CancelCallbackRan);
    }

    private sealed class ReentrantCancelViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        private INavigationService? _nav;
        private bool _reenterDispose;
        public bool CancelCallbackRan { get; private set; }

        public void EnableReentrantCancel(INavigationService nav) => _nav = nav;

        public void EnableReentrantDispose(INavigationService nav)
        {
            _nav = nav;
            _reenterDispose = true;
        }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            // Register a callback that reenters the service (takes the navigation lock).
            cancellationToken.Register(() =>
            {
                CancelCallbackRan = true;
                // This would deadlock if Cancel() ran while holding _navigationLock.
                if (_reenterDispose)
                {
                    ((IDisposable?)_nav)?.Dispose();
                }
                else
                {
                    _nav?.ClearCache("MainRegion");
                }
            });
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task Navigate_StateMismatchAbandonsAndReturnsFalse()
    {
        // If the stack top changes while the from-guard is running, the navigation
        // must be abandoned (return false) and its speculatively created scope released.
        StateMismatchTargetViewModel.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<StateMismatchGuardViewModel>();
        services.AddTransient<StateMismatchTargetViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<StateMismatchGuardViewModel>();
        var guardVm = Assert.IsType<StateMismatchGuardViewModel>(nav.GetCurrentViewModel());

        // First navigation blocks in the guard.
        var firstNav = Task.Run(() => nav.NavigateToAsync<StateMismatchTargetViewModel>());
        // Wait until the guard is actually running.
        await guardVm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Second navigation: guard lets it through immediately; stack top changes.
        guardVm.AllowThrough = true;
        var secondResult = await nav.NavigateToAsync<TestDetailViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(secondResult);

        // Release the first guard; it must detect the mismatch and abandon.
        guardVm.ReleaseGuard();
        var firstResult = await firstNav.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(firstResult);
        // The abandoned navigation's speculatively created scope was released, not leaked.
        Assert.Equal(1, StateMismatchTargetViewModel.DisposeCount);
        // Active page is the second navigation's target.
        Assert.IsType<TestDetailViewModel>(nav.GetCurrentViewModel());
    }

    private sealed class StateMismatchGuardViewModel : NavigationViewModelBase, INavigationGuard
    {
        public readonly TaskCompletionSource GuardEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _guardRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool AllowThrough;

        public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            if (AllowThrough)
            {
                return true;
            }

            GuardEntered.TrySetResult();
            await _guardRelease.Task.ConfigureAwait(false);
            return true;
        }

        public void ReleaseGuard() => _guardRelease.TrySetResult();
    }

    private sealed class StateMismatchTargetViewModel : NavigationViewModelBase, IDisposable
    {
        public static int DisposeCount;

        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCount);
        }
    }

    #endregion
}
