using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(AsyncDemoViewModel))]
public partial class AsyncDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<AsyncDemoViewModel>
{
    public AsyncDemoView() => InitializeComponent();
}
