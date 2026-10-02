using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using WpfSample.ViewModels;

namespace WpfSample.Views;

public partial class DashboardView : ElegantSeries.Flow.WPF.Views.BaseView<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Nested hosts need the same service + locator as the top-level ones.
        var navigation = App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();
        foreach (var host in new[] { Q1Host, Q2Host, Q3Host, Q4Host })
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        foreach (var host in new[] { Q1Host, Q2Host, Q3Host, Q4Host })
            host.Dispose();
    }
}
