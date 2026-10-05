using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(ReplaceDemoViewModel))]
public partial class ReplaceDemoView : ElegantSeries.Flow.Avalonia.Views.BaseView<ReplaceDemoViewModel>
{
    public ReplaceDemoView() => InitializeComponent();
}
