using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

/// <summary>
/// Carries <see cref="ViewForAttribute"/> as the compile-time view→ViewModel
/// declaration for the future source generator. The runtime still resolves
/// views through the manual <c>views.Register&lt;HomeView, HomeViewModel&gt;()</c>
/// call in <c>App</c> (the runtime never scans the attribute); when the
/// generator ships, the manual call is removed and the generated registration
/// takes over — never both (duplicate registration throws at startup).
/// </summary>
[ViewFor(typeof(HomeViewModel))]
public partial class HomeView : ElegantSeries.Flow.WPF.Views.BaseView<HomeViewModel>
{
    public HomeView() => InitializeComponent();
}
