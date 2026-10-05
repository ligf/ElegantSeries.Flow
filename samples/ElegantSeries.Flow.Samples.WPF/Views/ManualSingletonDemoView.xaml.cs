using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

// No [ViewFor] here: registered manually in App via
// views.RegisterSingleton<ManualSingletonDemoView, ManualSingletonDemoViewModel>().
public partial class ManualSingletonDemoView : ElegantSeries.Flow.WPF.Views.BaseView<ManualSingletonDemoViewModel>
{
    public ManualSingletonDemoView() => InitializeComponent();
}
