using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(StackChildViewModel))]
public partial class StackChildView : ElegantSeries.Flow.Avalonia.Views.BaseView<StackChildViewModel>
{
    public StackChildView() => InitializeComponent();
}
