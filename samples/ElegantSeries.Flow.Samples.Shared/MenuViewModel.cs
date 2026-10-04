using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Sidebar menu. Registered as <b>singleton</b> to demonstrate that singleton
/// ViewModels work fine: the page scope resolves the shared root instance and
/// never disposes it.
/// </summary>
public sealed partial class MenuViewModel : BaseViewModel
{
    [RelayCommand]
    private Task GoHomeAsync()
        => NavigateToAsync<HomeViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("Hello from Menu", RegionNames.MainRegion);

    [RelayCommand]
    private Task GoCounterAsync()
        => NavigateToAsync<CounterViewModel>(RegionNames.MainRegion, NavigationMode.KeepAlive);

    [RelayCommand]
    private Task GoQuadrantsAsync()
        => NavigateToAsync<QuadrantsViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoGuardedAsync()
        => NavigateToAsync<GuardedViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoAsyncDemoAsync()
        => NavigateToAsync<AsyncDemoViewModel, string>("Async demo parameter", RegionNames.MainRegion);

    [RelayCommand]
    private Task GoCacheDemoAsync()
        => NavigateToAsync<CacheDemoViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoCancelDemoAsync()
        => NavigateToAsync<CancelDemoViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoAsyncDisposeDemoAsync()
        => NavigateToAsync<AsyncDisposeDemoViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoFactoryDemoAsync()
        => NavigateToAsync<FactoryDemoViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoViewFailureDemoAsync()
        => NavigateToAsync<ViewFailureViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoRegionSwitchDemoAsync()
        => NavigateToAsync<RegionSwitchDemoViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoStateInspectorAsync()
        => NavigateToAsync<StateInspectorViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoEventsDemoAsync()
        => NavigateToAsync<EventsDemoViewModel>(RegionNames.MainRegion);

    [RelayCommand]
    private Task GoToolkitFreeDemoAsync()
        => NavigateToAsync<ToolkitFreeDemoViewModel>(RegionNames.MainRegion);

    /// <summary>
    /// Raised when the user asks for a second window. Window creation is
    /// platform-specific, so the host app wires this up (see App startup):
    /// the shared ViewModel only expresses the intent.
    /// </summary>
    public event Action? OpenSecondWindowRequested;

    [RelayCommand]
    private void OpenSecondWindow() => OpenSecondWindowRequested?.Invoke();
}
