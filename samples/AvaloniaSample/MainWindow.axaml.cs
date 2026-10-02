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
        // Failures are observed instead of fire-and-forget.
        Observe(navigation.NavigateToAsync<MenuViewModel>("Sidebar"));
        Observe(navigation.NavigateToAsync<HomeViewModel>("MainRegion"));
    }

    private async void Observe(Task<bool> navigation)
    {
        try
        {
            await navigation;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Navigation failed: {ex.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SidebarHost.Dispose();
        MainHost.Dispose();
        base.OnClosed(e);
    }
}
