using WpfSample.ViewModels;

namespace WpfSample.Views;

public partial class CounterView : ElegantSeries.Flow.WPF.Views.BaseView<CounterViewModel>
{
    public CounterView() => InitializeComponent();
}
