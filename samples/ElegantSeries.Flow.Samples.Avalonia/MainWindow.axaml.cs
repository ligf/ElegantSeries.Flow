using Avalonia.Controls;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.Avalonia.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia;

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

    private void ReportError(string message) => ErrorText.Text = message;

    protected override void OnClosed(EventArgs e)
    {
        SidebarHost.Dispose();
        MainHost.Dispose();
        base.OnClosed(e);
    }
}
