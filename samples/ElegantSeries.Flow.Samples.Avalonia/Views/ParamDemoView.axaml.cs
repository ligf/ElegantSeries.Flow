using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(ParamDemoViewModel))]
public partial class ParamDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<ParamDemoViewModel>
{
    public ParamDemoView() => InitializeComponent();
}
