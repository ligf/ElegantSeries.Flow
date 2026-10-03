using Avalonia.Controls;
using ElegantSeries.Flow.Avalonia.Hosting;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.Avalonia.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia;

public partial class MainWindow : Window
{
    private readonly NavigationHost[] _hosts;

    public MainWindow()
    {
        InitializeComponent();

        var navigation = App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();

        // All regions share the singleton navigation service but keep
        // independent stacks.
        _hosts = [SidebarHost, Q1Host, Q2Host, Q3Host, Q4Host];
        foreach (var host in _hosts)
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }

        // Navigate after the hosts are attached so the first pages are shown.
        // Failures are observed instead of fire-and-forget.
        // Q1 is the quadrant the menu drives; Q2-Q4 keep independent content
        // to prove regions navigate in isolation. Q1 and Q4 show the same
        // ViewModel *type* to prove each region keeps its own instance.
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<MenuViewModel>("Sidebar"), ReportError);
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<HomeViewModel>("Q1"), ReportError);
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<CounterViewModel>("Q2", NavigationMode.KeepAlive), ReportError);
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<DetailViewModel, string>("Top-right detail", "Q3"), ReportError);
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<HomeViewModel>("Q4"), ReportError);
    }

    private void ReportError(string message) => ErrorText.Text = message;

    protected override void OnClosed(EventArgs e)
    {
        foreach (var host in _hosts)
            host.Dispose();
        base.OnClosed(e);
    }
}
