using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(ViewFailureViewModel))]
public partial class ViewFailureView : ElegantSeries.Flow.Avalonia.Views.BaseView<ViewFailureViewModel>
{
    public ViewFailureView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Same pattern as QuadrantsView: the host needs the navigation service
        // and the view locator before the region is navigated.
        var navigation = ViewModel?.Navigation
            ?? App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();

        FailureHost.NavigationService = navigation;
        FailureHost.ViewLocator = views;
    }

    private void OnDetached(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        FailureHost.Dispose();
    }
}
