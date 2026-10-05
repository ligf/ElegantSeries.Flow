using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

// No [ViewFor] here: registered manually in App via
// views.RegisterTransient<ManualTransientDemoView, ManualTransientDemoViewModel>().
public partial class ManualTransientDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<ManualTransientDemoViewModel>
{
    public ManualTransientDemoView() => InitializeComponent();
}
