using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(KeepAliveTempViewModel))]
public partial class KeepAliveTempView : ElegantSeries.Flow.WPF.Views.BaseView<KeepAliveTempViewModel>
{
    public KeepAliveTempView() => InitializeComponent();
}
