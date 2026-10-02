using WpfSample.ViewModels;

namespace WpfSample.Views;

public partial class GuardedView : ElegantSeries.Flow.WPF.Views.BaseView<GuardedViewModel>
{
    public GuardedView() => InitializeComponent();
}
