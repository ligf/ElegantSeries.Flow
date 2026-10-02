using Avalonia.Controls;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaSample.ViewModels;

namespace AvaloniaSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var navigation = App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();

        // Both regions share the singleton navigation service but keep
        // independent stacks.
        SidebarHost.NavigationService = navigation;
        SidebarHost.ViewLocator = views;
        MainHost.NavigationService = navigation;
        MainHost.ViewLocator = views;

        // Navigate after the hosts are attached so the first pages are shown.
        _ = navigation.NavigateToAsync<MenuViewModel>("Sidebar");
        _ = navigation.NavigateToAsync<HomeViewModel>("MainRegion");
    }

    protected override void OnClosed(EventArgs e)
    {
        SidebarHost.Dispose();
        MainHost.Dispose();
        base.OnClosed(e);
    }
}
