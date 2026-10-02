using AvaloniaSample.ViewModels;

namespace AvaloniaSample.Views;

public partial class CounterView : ElegantSeries.Flow.Avalonia.Views.BaseView<CounterViewModel>
{
    public CounterView() => InitializeComponent();
}
