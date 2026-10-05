using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(KeepAliveDemoViewModel))]
public partial class KeepAliveDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<KeepAliveDemoViewModel>
{
    public KeepAliveDemoView() => InitializeComponent();
}
