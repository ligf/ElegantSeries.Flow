using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;

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
    }

    protected override void OnClosed(EventArgs e)
    {
        MainHost.Dispose(); // unsubscribes from the navigation service
        base.OnClosed(e);
    }
}
