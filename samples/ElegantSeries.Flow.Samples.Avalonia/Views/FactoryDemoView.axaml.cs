using ElegantSeries.Flow.Samples.Avalonia.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

public partial class FactoryDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<FactoryDemoViewModel>
{
    public FactoryDemoView() => InitializeComponent();

    /// <summary>
    /// Called by the custom view factory (see App): proves this instance was
    /// built by the factory, not by new TView().
    /// </summary>
    public void ApplyFactoryBadge()
        => FactoryBadgeText.Text = "Created by the custom view factory (not new TView()).";
}
