using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(Lifetime = ViewModelLifetime.ViewOnly)]
public partial class DetailView : ElegantSeries.Flow.Avalonia.Views.BaseView<DetailViewModel>
{
    public DetailView() => InitializeComponent();
}
