using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Mvvm.Tests;

/// <summary>
/// Pins the <see cref="BaseViewModel"/> navigation-attachment semantics:
/// the interface setter notifies, the silent setter does not, and the
/// protected navigation helpers delegate to the attached service (or
/// report <see langword="false"/> when no service is attached).
/// </summary>
public class BaseViewModelTests
{
    public sealed class TestViewModel : BaseViewModel
    {
        public Task<bool> NavigateToTarget() => NavigateToAsync<TargetViewModel>();

        public Task<bool> GoBack() => GoBackAsync();
    }

    public sealed class TargetViewModel : BaseViewModel
    {
    }

    private static List<string?> RecordPropertyChanges(BaseViewModel viewModel)
    {
        var names = new List<string?>();
        viewModel.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }

    [Fact]
    public void InterfaceNavigationSet_RaisesPropertyChangedOnce()
    {
        var viewModel = new TestViewModel();
        var stub = new StubNavigationService();
        var names = RecordPropertyChanges(viewModel);

        ((INavigationViewModel)viewModel).Navigation = stub;

        Assert.Same(stub, viewModel.Navigation);
        Assert.Equal(["Navigation"], names);
    }

    [Fact]
    public void InterfaceNavigationSet_SameValue_RaisesNoSecondNotification()
    {
        var viewModel = new TestViewModel();
        var stub = new StubNavigationService();
        var names = RecordPropertyChanges(viewModel);

        ((INavigationViewModel)viewModel).Navigation = stub;
        ((INavigationViewModel)viewModel).Navigation = stub;

        Assert.Equal(["Navigation"], names);
    }

    [Fact]
    public void SetNavigationSilent_SetsWithoutNotification_ThenRaiseNavigationChangedNotifies()
    {
        var viewModel = new TestViewModel();
        var stub = new StubNavigationService();
        var names = RecordPropertyChanges(viewModel);

        ((INavigationViewModel)viewModel).SetNavigationSilent(stub);

        Assert.Same(stub, viewModel.Navigation);
        Assert.Empty(names);

        ((INavigationViewModel)viewModel).RaiseNavigationChanged();

        Assert.Equal(["Navigation"], names);
    }

    [Fact]
    public void SetNavigationSilent_ToNull_ClearsWithoutNotification()
    {
        var viewModel = new TestViewModel();
        var stub = new StubNavigationService();
        ((INavigationViewModel)viewModel).Navigation = stub;
        var names = RecordPropertyChanges(viewModel);

        ((INavigationViewModel)viewModel).SetNavigationSilent(null);

        Assert.Null(viewModel.Navigation);
        Assert.Empty(names);
    }

    [Fact]
    public async Task ProtectedNavigation_WithoutService_ReturnsFalse()
    {
        var viewModel = new TestViewModel();

        Assert.False(await viewModel.NavigateToTarget());
        Assert.False(await viewModel.GoBack());
    }

    [Fact]
    public async Task NavigateToAsync_WithService_DelegatesAndReturnsFate()
    {
        var viewModel = new TestViewModel();
        var stub = new StubNavigationService { NavigateResult = true };
        ((INavigationViewModel)viewModel).SetNavigationSilent(stub);

        Assert.True(await viewModel.NavigateToTarget());
        Assert.Equal(1, stub.NavigateToCallCount);
        Assert.Equal("MainRegion", stub.LastNavigateRegion);
    }

    [Fact]
    public async Task GoBackAsync_WithService_DelegatesAndReturnsFate()
    {
        var viewModel = new TestViewModel();
        var stub = new StubNavigationService { GoBackResult = false };
        ((INavigationViewModel)viewModel).SetNavigationSilent(stub);

        Assert.False(await viewModel.GoBack());
        Assert.Equal(1, stub.GoBackCallCount);
        Assert.Equal("MainRegion", stub.LastGoBackRegion);
    }

    /// <summary>
    /// Minimal <see cref="INavigationService"/> stub: only the two
    /// navigation entry points <see cref="BaseViewModel"/> delegates to
    /// are functional; every other member throws.
    /// </summary>
    private sealed class StubNavigationService : INavigationService
    {
        public bool NavigateResult { get; set; } = true;
        public bool GoBackResult { get; set; } = true;
        public int NavigateToCallCount { get; private set; }
        public string? LastNavigateRegion { get; private set; }
        public int GoBackCallCount { get; private set; }
        public string? LastGoBackRegion { get; private set; }

        public Task<bool> NavigateToAsync<TViewModel>(
            string regionName = "MainRegion",
            NavigationOperation operation = NavigationOperation.Push,
            NavigationRetention retention = NavigationRetention.Destroy,
            bool refreshIfActive = false,
            CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel
        {
            NavigateToCallCount++;
            LastNavigateRegion = regionName;
            return Task.FromResult(NavigateResult);
        }

        public Task<bool> GoBackAsync(
            string regionName = "MainRegion",
            CancellationToken cancellationToken = default)
        {
            GoBackCallCount++;
            LastGoBackRegion = regionName;
            return Task.FromResult(GoBackResult);
        }

        public event Action<string, INavigationViewModel>? RegionNavigated { add { } remove { } }

        public event Action<string, INavigationViewModel>? ViewModelReleased { add { } remove { } }

        public event Action<string>? RegionCacheCleared { add { } remove { } }

        public event Action<Exception>? SubscriberError { add { } remove { } }

        public IDisposable SubscribeRegionNavigated(Action<string, INavigationViewModel> handler) =>
            throw new NotSupportedException();

        public IDisposable SubscribeViewModelReleased(Action<string, INavigationViewModel> handler) =>
            throw new NotSupportedException();

        public IDisposable SubscribeRegionCacheCleared(Action<string> handler) =>
            throw new NotSupportedException();

        public IDisposable SubscribeSubscriberError(Action<Exception> handler) =>
            throw new NotSupportedException();

        public bool IsInLifecycleCallback => throw new NotSupportedException();

        public bool IsInScopeDisposal => throw new NotSupportedException();

        public bool CanGoBack(string regionName = "MainRegion") => throw new NotSupportedException();

        public INavigationViewModel? GetCurrentViewModel(string regionName = "MainRegion") =>
            throw new NotSupportedException();

        public bool IsActive<TViewModel>(string regionName = "MainRegion")
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public NavigationRetention? GetCurrentRetention(string regionName = "MainRegion") =>
            throw new NotSupportedException();

        public object? GetCurrentParameter(INavigationViewModel viewModel, string regionName = "MainRegion") =>
            throw new NotSupportedException();

        public IReadOnlyList<INavigationViewModel> GetNavigationStack(string regionName = "MainRegion") =>
            throw new NotSupportedException();

        public IReadOnlyDictionary<string, IReadOnlyList<INavigationViewModel>> GetAllNavigationStacks() =>
            throw new NotSupportedException();

        public Task<bool> NavigateToAsync<TViewModel, TParam>(
            TParam parameter,
            string regionName = "MainRegion",
            NavigationOperation operation = NavigationOperation.Push,
            NavigationRetention retention = NavigationRetention.Destroy,
            bool refreshIfActive = false,
            CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public Task<bool> GoBackToAsync<TViewModel>(
            string regionName = "MainRegion",
            CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public Task<bool> GoBackToAsync<TViewModel, TParam>(
            TParam parameter,
            string regionName = "MainRegion",
            CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public Task<bool> SetRootAsync<TViewModel>(
            string regionName = "MainRegion",
            NavigationRetention retention = NavigationRetention.Destroy,
            CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public Task<bool> SetRootAsync<TViewModel, TParam>(
            TParam parameter,
            string regionName = "MainRegion",
            NavigationRetention retention = NavigationRetention.Destroy,
            CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public int Remove<TViewModel>(string regionName = "MainRegion")
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public int Remove<TViewModel, TParam>(TParam parameter, string regionName = "MainRegion")
            where TViewModel : INavigationViewModel => throw new NotSupportedException();

        public void ClearCache(string regionName) => throw new NotSupportedException();

        public void ClearAllCache() => throw new NotSupportedException();

        public ValueTask ClearCacheAsync(string regionName) => throw new NotSupportedException();

        public ValueTask ClearAllCacheAsync() => throw new NotSupportedException();
    }
}
