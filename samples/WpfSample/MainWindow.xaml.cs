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

        // Both regions share the singleton navigation service but keep
        // independent stacks.
        SidebarHost.NavigationService = navigation;
        SidebarHost.ViewLocator = views;
        MainHost.NavigationService = navigation;
        MainHost.ViewLocator = views;

        // Navigate after the hosts are attached so the first pages are shown.
        // Failures are observed instead of fire-and-forget.
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<MenuViewModel>("Sidebar"), ReportError);
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<HomeViewModel>("MainRegion"), ReportError);
    }

    private void ReportError(string message) =>
        MessageBox.Show(message, "MainWindow", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected override void OnClosed(EventArgs e)
    {
        SidebarHost.Dispose();
        MainHost.Dispose();
        base.OnClosed(e);
    }
}
