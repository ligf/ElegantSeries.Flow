using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor]
public partial class GuardedView : ElegantSeries.Flow.Avalonia.Views.BaseView<GuardedViewModel>
{
    public GuardedView() => InitializeComponent();
}
