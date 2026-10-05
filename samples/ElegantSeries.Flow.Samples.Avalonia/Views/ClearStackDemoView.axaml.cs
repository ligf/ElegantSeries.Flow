using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(ClearStackDemoViewModel))]
public partial class ClearStackDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<ClearStackDemoViewModel>
{
    public ClearStackDemoView() => InitializeComponent();
}
