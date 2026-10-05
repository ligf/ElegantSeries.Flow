using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(CancelDemoViewModel))]
public partial class CancelDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<CancelDemoViewModel>
{
    public CancelDemoView() => InitializeComponent();
}
