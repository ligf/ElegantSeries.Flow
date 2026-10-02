using AvaloniaSample.ViewModels;
using ElegantSeries.Flow.Avalonia.Hosting;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaSample.Views;

public partial class DashboardView : ElegantSeries.Flow.Avalonia.Views.BaseView<DashboardViewModel>
{
    private readonly NavigationHost[] _hosts;

    public DashboardView()
    {
        InitializeComponent();
        _hosts = [Q1Host, Q2Host, Q3Host, Q4Host];
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private async void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
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

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        foreach (var host in _hosts)
            host.Dispose();
    }
}
