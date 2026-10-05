using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(ClearStackChildViewModel))]
public partial class ClearStackChildView : ElegantSeries.Flow.WPF.Views.BaseView<ClearStackChildViewModel>
{
    public ClearStackChildView() => InitializeComponent();
}
