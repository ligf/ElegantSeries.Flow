using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Samples.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

public partial class RegionSwitchView : ElegantSeries.Flow.Avalonia.Views.BaseView<RegionSwitchDemoViewModel>
{
    public RegionSwitchView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // RegionName and NavigationService are data-bound in AXAML. ViewLocator
        // is platform-specific (WPF vs Avalonia), so it is assigned here —
        // the shared ViewModel cannot reference either interface.
        SwitchHost.ViewLocator = App.Services.GetRequiredService<IViewLocator>();
    }

    private void OnDetached(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        SwitchHost.Dispose();
    }
}
