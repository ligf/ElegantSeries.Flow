using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(RefreshDemoViewModel))]
public partial class RefreshDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<RefreshDemoViewModel>
{
    public RefreshDemoView() => InitializeComponent();
}
