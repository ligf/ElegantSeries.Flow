using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(ReplaceTargetViewModel))]
public partial class ReplaceTargetView : ElegantSeries.Flow.WPF.Views.BaseView<ReplaceTargetViewModel>
{
    public ReplaceTargetView() => InitializeComponent();
}
