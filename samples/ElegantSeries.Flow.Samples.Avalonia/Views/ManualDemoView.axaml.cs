using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

// No [ViewFor] here: this view is registered manually in App via
// views.Register<ManualDemoView, ManualDemoViewModel>().
public partial class ManualDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<ManualDemoViewModel>
{
    public ManualDemoView() => InitializeComponent();
}
