using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(ClearStackChildViewModel))]
public partial class ClearStackChildView : ElegantSeries.Flow.Avalonia.Views.BaseView<ClearStackChildViewModel>
{
    public ClearStackChildView() => InitializeComponent();
}
