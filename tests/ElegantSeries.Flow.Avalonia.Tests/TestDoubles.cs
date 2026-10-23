using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;
using ElegantSeries.Flow.Avalonia.Threading;
using ElegantSeries.Flow.Avalonia.Views;

namespace ElegantSeries.Flow.Avalonia.Tests;

/// <summary>Test ViewModel (non-sealed so derivation semantics can be tested).</summary>
internal class TestViewModel : NavigationViewModelBase
{
}

/// <summary>Another test ViewModel.</summary>
internal sealed class OtherViewModel : NavigationViewModelBase
{
}

/// <summary>Third test ViewModel (for concurrency tests).</summary>
internal sealed class ThirdViewModel : NavigationViewModelBase
{
}

/// <summary>Fourth test ViewModel (for concurrency tests).</summary>
internal sealed class FourthViewModel : NavigationViewModelBase
{
}

/// <summary>Derived ViewModel used to pin exact-type lookup semantics.</summary>
internal sealed class DerivedViewModel : TestViewModel
{
}

/// <summary>ViewModel type intentionally left unregistered.</summary>
internal sealed class UnregisteredViewModel : NavigationViewModelBase
{
}

/// <summary>Plain control used as a registered view in locator tests.</summary>
internal sealed class TestView : Control
{
}

/// <summary>
/// <see cref="BaseView{TViewModel}"/> subclass that records
/// <c>OnViewModelChanged</c> invocations and exposes the typed ViewModel.
/// </summary>
internal sealed class RecordingView : BaseView<TestViewModel>
{
    public List<(TestViewModel? OldValue, TestViewModel? NewValue)> Changes { get; } = new();

    public TestViewModel? CurrentViewModel => ViewModel;

    protected override void OnViewModelChanged(TestViewModel? oldValue, TestViewModel? newValue)
        => Changes.Add((oldValue, newValue));
}

/// <summary>Controllable <see cref="IDispatcher"/> test double.</summary>
internal sealed class FakeDispatcher : IDispatcher
{
    public bool IsOnUiThread { get; set; } = true;

    public List<Action> PostedActions { get; } = new();

    public bool CheckAccess() => IsOnUiThread;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PostedActions.Add(action);
    }

    public void RunPosted()
    {
        foreach (var action in PostedActions)
        {
            action();
        }

        PostedActions.Clear();
    }
}

/// <summary><see cref="INavigationService"/> test double that raises events on demand.</summary>
internal sealed class FakeNavigationService : INavigationService
{
    public event Action<string, INavigationViewModel>? RegionNavigated;
    public event Action<string, INavigationViewModel>? ViewModelReleased;
    public event Action<string>? RegionCacheCleared;
    public event Action<Exception>? SubscriberError;

    public IDisposable SubscribeRegionNavigated(Action<string, INavigationViewModel> handler)
    {
        RegionNavigated += handler;
        return new FakeSubscription(() => RegionNavigated -= handler);
    }

    public IDisposable SubscribeViewModelReleased(Action<string, INavigationViewModel> handler)
    {
        ViewModelReleased += handler;
        return new FakeSubscription(() => ViewModelReleased -= handler);
    }

    public IDisposable SubscribeRegionCacheCleared(Action<string> handler)
    {
        RegionCacheCleared += handler;
        return new FakeSubscription(() => RegionCacheCleared -= handler);
    }

    public IDisposable SubscribeSubscriberError(Action<Exception> handler)
    {
        SubscriberError += handler;
        return new FakeSubscription(() => SubscriberError -= handler);
    }

    private sealed class FakeSubscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }

    public void RaiseSubscriberError(Exception ex)
        => SubscriberError?.Invoke(ex);

    public void RaiseRegionNavigated(string regionName, INavigationViewModel viewModel)
        => RegionNavigated?.Invoke(regionName, viewModel);

    public void RaiseViewModelReleased(string regionName, INavigationViewModel viewModel)
        => ViewModelReleased?.Invoke(regionName, viewModel);

    public void RaiseRegionCacheCleared(string regionName)
        => RegionCacheCleared?.Invoke(regionName);

    public bool CanGoBack(string regionName = "MainRegion") => false;

    public INavigationViewModel? GetCurrentViewModel(string regionName = "MainRegion") => null;

    public bool IsActive<TViewModel>(string regionName = "MainRegion") where TViewModel : INavigationViewModel => false;

    public NavigationRetention? GetCurrentRetention(string regionName = "MainRegion") => null;

    public IReadOnlyList<INavigationViewModel> GetNavigationStack(string regionName = "MainRegion") => [];

    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(string regionName = "MainRegion", NavigationOperation operation = NavigationOperation.Push, NavigationRetention retention = NavigationRetention.Destroy, bool refreshIfActive = false, CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => throw new NotImplementedException();

    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(TParam parameter, string regionName = "MainRegion", NavigationOperation operation = NavigationOperation.Push, NavigationRetention retention = NavigationRetention.Destroy, bool refreshIfActive = false, CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => throw new NotImplementedException();

    public Task<bool> GoBackAsync(string regionName = "MainRegion", CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public void ClearCache(string regionName) => throw new NotImplementedException();

    public void ClearAllCache() => throw new NotImplementedException();

    public ValueTask ClearCacheAsync(string regionName) => throw new NotImplementedException();

    public ValueTask ClearAllCacheAsync() => throw new NotImplementedException();
}
