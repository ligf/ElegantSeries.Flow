using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

/// <summary>
/// Carries <see cref="ViewForAttribute"/> as the compile-time view→ViewModel
/// declaration. The ElegantSeries.Flow source generator turns it into the
/// <c>views.Register&lt;HomeView, HomeViewModel&gt;()</c> call inside
/// <c>RegisterAttributedViews()</c> (the runtime never scans the attribute).
/// <c>Lifetime.ViewOnly</c> keeps the ViewModel's DI registration manual
/// (see <c>App</c>); never register the same ViewModel both manually and via
/// the attribute — duplicate registration throws at startup.
/// </summary>
[ViewFor(Lifetime = ViewModelLifetime.ViewOnly)]
public partial class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<HomeViewModel>
{
    public HomeView() => InitializeComponent();
}
