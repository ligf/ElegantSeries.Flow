using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(ParamReceiverViewModel))]
public partial class ParamReceiverView : ElegantSeries.Flow.WPF.Views.BaseView<ParamReceiverViewModel>
{
    public ParamReceiverView() => InitializeComponent();
}
