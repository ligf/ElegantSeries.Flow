using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(ReplaceTargetViewModel))]
public partial class ReplaceTargetView : ElegantSeries.Flow.Avalonia.Views.BaseView<ReplaceTargetViewModel>
{
    public ReplaceTargetView() => InitializeComponent();
}
