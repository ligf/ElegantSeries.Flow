using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(CounterViewModel))]
public partial class CounterView : ElegantSeries.Flow.Avalonia.Views.BaseView<CounterViewModel>
{
    public CounterView() => InitializeComponent();
}
