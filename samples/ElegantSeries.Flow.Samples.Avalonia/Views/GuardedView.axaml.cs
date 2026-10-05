using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(GuardedViewModel))]
public partial class GuardedView : ElegantSeries.Flow.Avalonia.Views.BaseView<GuardedViewModel>
{
    public GuardedView() => InitializeComponent();
}
