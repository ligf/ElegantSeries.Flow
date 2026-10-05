using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor]
public partial class CounterView : ElegantSeries.Flow.Avalonia.Views.BaseView<CounterViewModel>
{
    public CounterView() => InitializeComponent();
}
