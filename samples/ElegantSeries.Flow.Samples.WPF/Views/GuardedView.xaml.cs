using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(GuardedViewModel))]
public partial class GuardedView : ElegantSeries.Flow.WPF.Views.BaseView<GuardedViewModel>
{
    public GuardedView() => InitializeComponent();
}
