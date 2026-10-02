using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Hosting;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using WpfSample.ViewModels;

namespace WpfSample.Views;

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

        // Hosts do not replay missed events, so quadrants are (re-)navigated
        // every time the view attaches: refreshIfActive re-activates the
        // already-active page (raising RegionNavigated for the fresh host)
        // instead of pushing a duplicate. Exceptions are observed.
        try
        {
            await navigation.NavigateToAsync<HomeViewModel>("Q1", refreshIfActive: true);
            await navigation.NavigateToAsync<CounterViewModel>("Q2", NavigationMode.KeepAlive, refreshIfActive: true);
            await navigation.NavigateToAsync<DetailViewModel, string>("Top-right detail", "Q3", refreshIfActive: true);
            await navigation.NavigateToAsync<HomeViewModel>("Q4", refreshIfActive: true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Quadrant navigation failed: {ex.Message}";
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
