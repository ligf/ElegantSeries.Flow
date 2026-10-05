using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

// No [ViewFor] here: registered manually in App via
// views.RegisterTransient<ManualTransientDemoView, ManualTransientDemoViewModel>().
public partial class ManualTransientDemoView : ElegantSeries.Flow.WPF.Views.BaseView<ManualTransientDemoViewModel>
{
    public ManualTransientDemoView() => InitializeComponent();
}
