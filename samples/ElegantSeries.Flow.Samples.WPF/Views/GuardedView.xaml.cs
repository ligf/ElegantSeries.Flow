using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor]
public partial class GuardedView : ElegantSeries.Flow.WPF.Views.BaseView<GuardedViewModel>
{
    public GuardedView() => InitializeComponent();
}
