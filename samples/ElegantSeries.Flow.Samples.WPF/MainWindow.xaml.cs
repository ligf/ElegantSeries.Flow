using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Hosting;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.WPF.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF;

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

    private void ReportError(string message) =>
        MessageBox.Show(message, "MainWindow", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected override void OnClosed(EventArgs e)
    {
        // Best-effort: every host is disposed even if one of them throws,
        // mirroring the navigation service's own disposal semantics.
        List<Exception>? errors = null;
        foreach (var host in _hosts)
        {
            try
            {
                host.Dispose();
            }
            catch (Exception ex)
            {
                errors ??= [];
                errors.Add(ex);
            }
        }

        base.OnClosed(e);

        if (errors is { Count: 1 })
        {
            throw errors[0];
        }

        if (errors is { Count: > 1 })
        {
            throw new AggregateException("One or more navigation hosts failed to dispose.", errors);
        }
    }
}
