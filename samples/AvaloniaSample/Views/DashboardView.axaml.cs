using AvaloniaSample.ViewModels;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaSample.Views;

public partial class DashboardView : ElegantSeries.Flow.Avalonia.Views.BaseView<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Nested hosts need the same service + locator as the top-level ones.
        var navigation = App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();
        foreach (var host in new[] { Q1Host, Q2Host, Q3Host, Q4Host })
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        foreach (var host in new[] { Q1Host, Q2Host, Q3Host, Q4Host })
            host.Dispose();
    }
}
