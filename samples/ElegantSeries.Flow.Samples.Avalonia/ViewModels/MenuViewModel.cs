using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Sidebar menu. Registered as <b>singleton</b> to demonstrate that singleton
/// ViewModels work fine: the page scope resolves the shared root instance and
/// never disposes it.
/// </summary>
public sealed partial class MenuViewModel : BaseViewModel
{
    [RelayCommand]
    private Task GoHomeAsync()
        => NavigateToAsync<HomeViewModel>("MainRegion");

    [RelayCommand]
    private Task GoDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("Hello from Menu", "MainRegion");

    [RelayCommand]
    private Task GoCounterAsync()
        => NavigateToAsync<CounterViewModel>("MainRegion", NavigationMode.KeepAlive);

    [RelayCommand]
    private Task GoQuadrantsAsync()
        => NavigateToAsync<QuadrantsViewModel>("MainRegion");

    [RelayCommand]
    private Task GoGuardedAsync()
        => NavigateToAsync<GuardedViewModel>("MainRegion");

    [RelayCommand]
    private Task GoAsyncDemoAsync()
        => NavigateToAsync<AsyncDemoViewModel>("MainRegion");

    [RelayCommand]
    private Task GoCacheDemoAsync()
        => NavigateToAsync<CacheDemoViewModel>("MainRegion");

    [RelayCommand]
    private Task GoCancelDemoAsync()
        => NavigateToAsync<CancelDemoViewModel>("MainRegion");

    [RelayCommand]
    private Task GoAsyncDisposeDemoAsync()
        => NavigateToAsync<AsyncDisposeDemoViewModel>("MainRegion");

    [RelayCommand]
    private Task GoFactoryDemoAsync()
        => NavigateToAsync<FactoryDemoViewModel>("MainRegion");

    [RelayCommand]
    private Task GoViewFailureDemoAsync()
        => NavigateToAsync<ViewFailureViewModel>("MainRegion");

    [RelayCommand]
    private void OpenSecondWindow() => new SecondWindow().Show();
}
