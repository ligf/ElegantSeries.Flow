using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(ClearStackDemoViewModel))]
public partial class ClearStackDemoView : ElegantSeries.Flow.WPF.Views.BaseView<ClearStackDemoViewModel>
{
    public ClearStackDemoView() => InitializeComponent();
}
