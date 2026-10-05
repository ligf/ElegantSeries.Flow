using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(CacheDemoViewModel))]
public partial class CacheDemoView : ElegantSeries.Flow.WPF.Views.BaseView<CacheDemoViewModel>
{
    public CacheDemoView() => InitializeComponent();
}
