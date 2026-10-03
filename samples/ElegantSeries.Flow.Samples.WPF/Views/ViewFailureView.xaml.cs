using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.WPF.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.Views;

public partial class ViewFailureView : ElegantSeries.Flow.WPF.Views.BaseView<ViewFailureViewModel>
{
    public ViewFailureView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Same pattern as QuadrantsView: the host needs the navigation service
        // and the view locator before the region is navigated.
        var navigation = ViewModel?.Navigation
            ?? App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();

        FailureHost.NavigationService = navigation;
        FailureHost.ViewLocator = views;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        FailureHost.Dispose();
    }
}
