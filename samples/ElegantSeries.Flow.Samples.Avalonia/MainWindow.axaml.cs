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

        // Both regions share the singleton navigation service but keep
        // independent stacks.
        _hosts = [SidebarHost, MainHost];
        foreach (var host in _hosts)
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }

        // Navigate after the hosts are attached so the first pages are shown.
        // Failures are observed instead of fire-and-forget.
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<MenuViewModel>("Sidebar"), ReportError);
        SampleHelpers.ObserveNavigation(navigation.NavigateToAsync<HomeViewModel>("MainRegion"), ReportError);
    }

    private void ReportError(string message) => ErrorText.Text = message;

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
