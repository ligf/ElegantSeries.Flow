using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor]
public partial class AsyncDisposeView : ElegantSeries.Flow.WPF.Views.BaseView<AsyncDisposeDemoViewModel>
{
    public AsyncDisposeView() => InitializeComponent();
}
