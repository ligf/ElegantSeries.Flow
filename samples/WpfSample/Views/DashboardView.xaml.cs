using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using WpfSample.ViewModels;

namespace WpfSample.Views;

public partial class DashboardView : ElegantSeries.Flow.WPF.Views.BaseView<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();
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

        foreach (var host in new[] { Q1Host, Q2Host, Q3Host, Q4Host })
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }

        // Navigate quadrants only after the nested hosts are attached (they
        // do not replay missed events), and only into empty regions.
        // Exceptions are observed instead of fire-and-forget.
        try
        {
            if (navigation.GetCurrentViewModel("Q1") is null)
                await navigation.NavigateToAsync<HomeViewModel>("Q1");
            if (navigation.GetCurrentViewModel("Q2") is null)
                await navigation.NavigateToAsync<CounterViewModel>("Q2", NavigationMode.KeepAlive);
            if (navigation.GetCurrentViewModel("Q3") is null)
                await navigation.NavigateToAsync<DetailViewModel, string>("Top-right detail", "Q3");
            if (navigation.GetCurrentViewModel("Q4") is null)
                await navigation.NavigateToAsync<HomeViewModel>("Q4");
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
        foreach (var host in new[] { Q1Host, Q2Host, Q3Host, Q4Host })
            host.Dispose();
    }
}
