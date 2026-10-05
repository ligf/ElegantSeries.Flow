using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(ReplaceDemoViewModel))]
public partial class ReplaceDemoView : ElegantSeries.Flow.WPF.Views.BaseView<ReplaceDemoViewModel>
{
    public ReplaceDemoView() => InitializeComponent();
}
