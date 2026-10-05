using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(AsyncDemoViewModel))]
public partial class AsyncDemoView : ElegantSeries.Flow.WPF.Views.BaseView<AsyncDemoViewModel>
{
    public AsyncDemoView() => InitializeComponent();
}
