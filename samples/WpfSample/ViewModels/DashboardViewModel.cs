using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace WpfSample.ViewModels;

/// <summary>
/// Demonstrates <b>nested regions</b>: the dashboard view hosts four
/// NavigationHosts (Q1-Q4), each navigating independently.
/// Q1 and Q4 show the same ViewModel <i>type</i> to prove each region keeps
/// its own instance.
/// </summary>
public sealed partial class DashboardViewModel : BaseViewModel, INavigationAware
{
    public void OnNavigatedTo(object? parameter)
    {
        // Each quadrant navigates independently.
        _ = NavigateToAsync<HomeViewModel>("Q1");
        _ = NavigateToAsync<CounterViewModel>("Q2", NavigationMode.KeepAlive);
        _ = NavigateToAsync<DetailViewModel, string>("Top-right detail", "Q3");
        _ = NavigateToAsync<HomeViewModel>("Q4");
    }

    public void OnNavigatedFrom() { }
}
