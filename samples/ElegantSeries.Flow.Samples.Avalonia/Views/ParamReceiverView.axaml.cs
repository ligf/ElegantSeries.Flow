using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(ParamReceiverViewModel))]
public partial class ParamReceiverView : ElegantSeries.Flow.Avalonia.Views.BaseView<ParamReceiverViewModel>
{
    public ParamReceiverView() => InitializeComponent();
}
