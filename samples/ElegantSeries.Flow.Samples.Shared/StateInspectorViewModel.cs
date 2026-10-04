using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Read-only navigation-state inspection: <see cref="INavigationService.CanGoBack"/>,
/// <see cref="INavigationService.IsActive{TViewModel}"/>,
/// <see cref="INavigationService.GetCurrentMode"/>,
/// <see cref="INavigationService.GetCurrentViewModel"/> and
/// <c>IViewLocator.IsRegistered{T}</c>. Navigate around with the buttons, then
/// hit Refresh. (The registration flags are supplied by the platform view's
/// code-behind: <c>IViewLocator</c> is platform-specific, so this shared
/// ViewModel cannot reference it.)
/// </summary>
public sealed partial class StateInspectorViewModel : BaseViewModel, INavigationAware
{
    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _isHomeActive;

    [ObservableProperty]
    private bool _isCounterActive;

    [ObservableProperty]
    private string _currentMode = "-";

    [ObservableProperty]
    private string _currentViewModel = "-";

    [ObservableProperty]
    private bool _isHomeRegistered;

    [ObservableProperty]
    private bool _isUnregisteredDemoRegistered;

    public void OnNavigatedTo(object? parameter) => RefreshState();

    public void OnNavigatedFrom() { }

    /// <summary>
    /// Called by the platform view (which can resolve the platform-specific
    /// <c>IViewLocator</c>) to report registration query results.
    /// </summary>
    public void ReportRegistrations(bool homeRegistered, bool unregisteredDemoRegistered)
    {
        IsHomeRegistered = homeRegistered;
        IsUnregisteredDemoRegistered = unregisteredDemoRegistered;
    }

    [RelayCommand]
    private void RefreshState()
    {
        var navigation = Navigation;
        CanGoBack = navigation?.CanGoBack(RegionNames.MainRegion) ?? false;
        IsHomeActive = navigation?.IsActive<HomeViewModel>(RegionNames.MainRegion) ?? false;
        IsCounterActive = navigation?.IsActive<CounterViewModel>(RegionNames.MainRegion) ?? false;
        CurrentMode = navigation?.GetCurrentMode(RegionNames.MainRegion)?.ToString() ?? "-";
        CurrentViewModel = navigation?.GetCurrentViewModel(RegionNames.MainRegion)?.GetType().Name ?? "-";
    }

    [RelayCommand]
    private Task PushHomeAsync()
        => NavigateToAsync<HomeViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task PushCounterAsync()
        => NavigateToAsync<CounterViewModel>(RegionNames.MainRegion, NavigationMode.KeepAlive);

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync();
}
