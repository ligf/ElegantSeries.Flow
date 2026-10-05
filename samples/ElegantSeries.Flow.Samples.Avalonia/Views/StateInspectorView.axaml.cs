using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(StateInspectorViewModel))]
public partial class StateInspectorView : ElegantSeries.Flow.Avalonia.Views.BaseView<StateInspectorViewModel>
{
    public StateInspectorView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // IViewLocator is platform-specific, so the shared ViewModel cannot
        // query it directly; the view reports the results back.
        var locator = App.Services.GetRequiredService<IViewLocator>();
        ViewModel?.ReportRegistrations(
            locator.IsRegistered<HomeViewModel>(),
            locator.IsRegistered<UnregisteredDemoViewModel>());
    }

    private void OnDetached(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
    }
}
