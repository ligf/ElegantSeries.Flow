using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(AsyncDisposeDemoViewModel))]
public partial class AsyncDisposeView : ElegantSeries.Flow.Avalonia.Views.BaseView<AsyncDisposeDemoViewModel>
{
    public AsyncDisposeView() => InitializeComponent();
}
