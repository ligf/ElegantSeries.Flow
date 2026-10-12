using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(Lifetime = ViewModelLifetime.Singleton)]
public partial class MenuView : ElegantSeries.Flow.WPF.Views.BaseView<MenuViewModel>
{
    public MenuView() => InitializeComponent();
}
