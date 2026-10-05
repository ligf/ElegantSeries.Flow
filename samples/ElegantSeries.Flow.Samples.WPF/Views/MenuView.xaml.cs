using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor(typeof(MenuViewModel), Lifetime = ViewModelLifetime.Singleton)]
public partial class MenuView : ElegantSeries.Flow.WPF.Views.BaseView<MenuViewModel>
{
    public MenuView() => InitializeComponent();
}
