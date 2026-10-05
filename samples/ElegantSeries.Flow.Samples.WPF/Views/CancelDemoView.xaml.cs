using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(CancelDemoViewModel))]
public partial class CancelDemoView : ElegantSeries.Flow.WPF.Views.BaseView<CancelDemoViewModel>
{
    public CancelDemoView() => InitializeComponent();
}
