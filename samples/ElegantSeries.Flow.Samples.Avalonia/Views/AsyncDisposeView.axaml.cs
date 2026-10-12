using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor]
public partial class AsyncDisposeView : ElegantSeries.Flow.Avalonia.Views.BaseView<AsyncDisposeDemoViewModel>
{
    public AsyncDisposeView() => InitializeComponent();
}
