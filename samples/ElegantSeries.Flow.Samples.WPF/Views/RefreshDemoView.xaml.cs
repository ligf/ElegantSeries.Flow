using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(RefreshDemoViewModel))]
public partial class RefreshDemoView : ElegantSeries.Flow.WPF.Views.BaseView<RefreshDemoViewModel>
{
    public RefreshDemoView() => InitializeComponent();
}
