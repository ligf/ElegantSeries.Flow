using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Demonstrates the navigation events <see cref="INavigationService.RegionNavigated"/>,
/// <see cref="INavigationService.ViewModelReleased"/> and
/// <see cref="INavigationService.RegionCacheCleared"/>. Everything happens in
/// <c>SwitchA</c> so this page stays active while it observes: push a temp page
/// (navigated), pop it (released — only a disposed scope raises the event, so
/// the temp page uses <c>New</c> mode), then push a KeepAlive page and clear its
/// cache (cache cleared).
/// </summary>
/// <remarks>
/// Subscribes in <c>OnNavigatedTo</c> and unsubscribes in <c>OnNavigatedFrom</c>
/// through the service reference captured on activation: the navigation service
/// clears <c>Navigation</c> <i>before</i> invoking <c>OnNavigatedFrom</c> (so a
/// deactivating page cannot start a nested navigation mid-transition), so the
/// property itself is already <see langword="null"/> there.
/// </remarks>
public sealed partial class EventsDemoViewModel : BaseViewModel, INavigationAware
{
    private INavigationService? _navigation;

    [ObservableProperty]
    private string _log = string.Empty;

    private void AppendLog(string line) => Log += line + "\n";

    public void OnNavigatedTo(object? parameter)
    {
        _navigation = Navigation;
        if (_navigation is null)
        {
            return;
        }

        _navigation.RegionNavigated += OnRegionNavigated;
        _navigation.ViewModelReleased += OnViewModelReleased;
        _navigation.RegionCacheCleared += OnRegionCacheCleared;
        AppendLog("Subscribed to RegionNavigated / ViewModelReleased / RegionCacheCleared.");
    }

    public void OnNavigatedFrom()
    {
        if (_navigation is null)
        {
            return;
        }

        _navigation.RegionNavigated -= OnRegionNavigated;
        _navigation.ViewModelReleased -= OnViewModelReleased;
        _navigation.RegionCacheCleared -= OnRegionCacheCleared;
        _navigation = null;
        AppendLog("Unsubscribed.");
    }

    private void OnRegionNavigated(string regionName, INavigationViewModel viewModel)
        => AppendLog($"RegionNavigated: region={regionName}, vm={viewModel.GetType().Name}");

    private void OnViewModelReleased(string regionName, INavigationViewModel viewModel)
        => AppendLog($"ViewModelReleased: region={regionName}, vm={viewModel.GetType().Name}");

    private void OnRegionCacheCleared(string regionName)
        => AppendLog($"RegionCacheCleared: region={regionName}");

    [RelayCommand]
    private Task PushTempAsync()
        => NavigateToAsync<StackChildViewModel, int>(42, RegionNames.SwitchA);

    [RelayCommand]
    private Task BackTempAsync()
        => GoBackAsync(RegionNames.SwitchA);

    [RelayCommand]
    private Task PushKeepAliveTempAsync()
        => NavigateToAsync<CounterViewModel>(RegionNames.SwitchA, NavigationMode.KeepAlive);

    [RelayCommand]
    private void ClearTempCache()
    {
        Navigation?.ClearCache(RegionNames.SwitchA);
    }
}
