using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor(Lifetime = ViewModelLifetime.Singleton)]
public partial class MenuView : ElegantSeries.Flow.Avalonia.Views.BaseView<MenuViewModel>
{
    public MenuView() => InitializeComponent();
}
