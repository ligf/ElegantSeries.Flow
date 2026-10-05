using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

// No [ViewFor] here: registered manually in App via
// views.RegisterSingleton<ManualSingletonDemoView, ManualSingletonDemoViewModel>().
public partial class ManualSingletonDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<ManualSingletonDemoViewModel>
{
    public ManualSingletonDemoView() => InitializeComponent();
}
