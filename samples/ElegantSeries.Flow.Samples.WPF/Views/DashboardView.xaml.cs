using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Hosting;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.WPF.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.Views;

public partial class DashboardView : ElegantSeries.Flow.WPF.Views.BaseView<DashboardViewModel>
{
    private readonly NavigationHost[] _hosts;

    public DashboardView()
    {
        InitializeComponent();
        _hosts = [Q1Host, Q2Host, Q3Host, Q4Host];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Use the NavigationService that owns this page (works in the main
        // window and in isolated second windows); fall back to the root.
        var navigation = ViewModel?.Navigation
            ?? App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();

        foreach (var host in _hosts)
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }

        // Hosts do not replay missed events, so quadrants are navigated after
        // the hosts attach: refreshIfActive re-activates the already-active
        // page (raising RegionNavigated for the fresh hosts) instead of
        // pushing a duplicate. Each navigation creates a new DashboardView,
        // so re-attaching the same instance is not supported: its hosts are
        // disposed on unload and a disposed host cannot be revived.
        // Each quadrant navigates independently: one failure must not block
        // the others.
        await TryNavigate("Q1", () => navigation.NavigateToAsync<HomeViewModel>("Q1", refreshIfActive: true));
        await TryNavigate("Q2", () => navigation.NavigateToAsync<CounterViewModel>("Q2", NavigationMode.KeepAlive, refreshIfActive: true));
        await TryNavigate("Q3", () => navigation.NavigateToAsync<DetailViewModel, string>("Top-right detail", "Q3", refreshIfActive: true));
        await TryNavigate("Q4", () => navigation.NavigateToAsync<HomeViewModel>("Q4", refreshIfActive: true));

        async Task TryNavigate(string region, Func<Task<bool>> navigate)
        {
            try
            {
                await navigate();
            }
            catch (Exception ex)
            {
                ErrorText.Text += $"[{region}: {ex.Message}] ";
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        foreach (var host in _hosts)
            host.Dispose();
    }
}
