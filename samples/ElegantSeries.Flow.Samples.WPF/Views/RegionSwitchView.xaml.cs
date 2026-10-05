using System.Windows;
using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;
using ElegantSeries.Flow.WPF.Hosting;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(RegionSwitchDemoViewModel))]
public partial class RegionSwitchView : ElegantSeries.Flow.WPF.Views.BaseView<RegionSwitchDemoViewModel>
{
    public RegionSwitchView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // RegionName and NavigationService are data-bound in XAML. ViewLocator
        // is platform-specific (WPF vs Avalonia), so it is assigned here —
        // the shared ViewModel cannot reference either interface.
        SwitchHost.ViewLocator = App.Services.GetRequiredService<IViewLocator>();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        SwitchHost.Dispose();
    }
}
