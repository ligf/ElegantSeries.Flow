using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Sidebar menu. Registered as <b>singleton</b> to demonstrate that singleton
/// ViewModels work fine: the page scope resolves the shared root instance and
/// never disposes it.
/// </summary>
public sealed partial class MenuViewModel : BaseViewModel
{
    [RelayCommand]
    private Task GoHomeAsync()
        => NavigateToAsync<HomeViewModel>("Q1");

    [RelayCommand]
    private Task GoDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("Hello from Menu", "Q1");

    [RelayCommand]
    private Task GoCounterAsync()
        => NavigateToAsync<CounterViewModel>("Q1", NavigationMode.KeepAlive);

    [RelayCommand]
    private Task GoGuardedAsync()
        => NavigateToAsync<GuardedViewModel>("Q1");

    [RelayCommand]
    private Task GoFeaturesAsync()
        => NavigateToAsync<FeaturesViewModel>("Q1");

    [RelayCommand]
    private void OpenSecondWindow() => new SecondWindow().Show();
}
