using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(typeof(MenuViewModel), Lifetime = ViewModelLifetime.Singleton)]
public partial class MenuView : ElegantSeries.Flow.Avalonia.Views.BaseView<MenuViewModel>
{
    public MenuView() => InitializeComponent();
}
