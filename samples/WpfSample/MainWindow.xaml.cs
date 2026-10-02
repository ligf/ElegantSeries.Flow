using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using WpfSample.ViewModels;

namespace WpfSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var navigation = App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();
        MainHost.NavigationService = navigation;
        MainHost.ViewLocator = views;

        // Navigate after the host is attached so the first page is shown.
        _ = navigation.NavigateToAsync<HomeViewModel>();
    }

    protected override void OnClosed(EventArgs e)
    {
        MainHost.Dispose(); // unsubscribes from the navigation service
        base.OnClosed(e);
    }
}
