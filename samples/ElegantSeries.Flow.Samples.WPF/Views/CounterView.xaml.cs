using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor]
public partial class CounterView : ElegantSeries.Flow.WPF.Views.BaseView<CounterViewModel>
{
    public CounterView() => InitializeComponent();
}
