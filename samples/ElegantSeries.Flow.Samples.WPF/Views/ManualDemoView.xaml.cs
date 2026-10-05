using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

// No [ViewFor] here: this view is registered manually in App via
// views.Register<ManualDemoView, ManualDemoViewModel>().
public partial class ManualDemoView : ElegantSeries.Flow.WPF.Views.BaseView<ManualDemoViewModel>
{
    public ManualDemoView() => InitializeComponent();
}
