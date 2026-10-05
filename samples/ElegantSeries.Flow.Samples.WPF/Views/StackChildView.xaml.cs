using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(StackChildViewModel))]
public partial class StackChildView : ElegantSeries.Flow.WPF.Views.BaseView<StackChildViewModel>
{
    public StackChildView() => InitializeComponent();
}
