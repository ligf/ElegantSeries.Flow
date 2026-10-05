using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(DetailViewModel), Lifetime = ViewModelLifetime.ViewOnly)]
public partial class DetailView : ElegantSeries.Flow.WPF.Views.BaseView<DetailViewModel>
{
    public DetailView() => InitializeComponent();
}
