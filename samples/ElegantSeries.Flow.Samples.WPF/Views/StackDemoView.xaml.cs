using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(StackDemoViewModel))]
public partial class StackDemoView : ElegantSeries.Flow.WPF.Views.BaseView<StackDemoViewModel>
{
    public StackDemoView() => InitializeComponent();
}
