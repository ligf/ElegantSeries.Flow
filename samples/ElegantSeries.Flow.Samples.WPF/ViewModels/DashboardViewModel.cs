using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Demonstrates <b>nested regions</b>: the dashboard view hosts four
/// NavigationHosts (Q1-Q4), each navigating independently.
/// Q1 and Q4 show the same ViewModel <i>type</i> to prove each region keeps
/// its own instance.
/// </summary>
public sealed partial class DashboardViewModel : BaseViewModel
{
    public string Title => "Dashboard (nested regions)";
}
