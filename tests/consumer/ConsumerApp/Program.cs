using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Avalonia.Extensions;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Avalonia.Views;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Generated;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.ViewModels;

// NuGet consumer smoke test: everything below uses ONLY the published
// packages (restored from the local feed — see nuget.config). It
// exercises the documented consumer flow end to end:
//   AddElegantSeriesFlow + AddElegantSeriesFlowViews with the source
//   generator's RegisterAttributedViews, navigation with the core
//   service, and a KeepAlive round trip. Any failure exits non-zero.

var failures = 0;
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}");
    if (!condition)
    {
        failures++;
    }
}

var services = new ServiceCollection();
services.AddElegantSeriesFlow();
services.AddTransient<AlphaViewModel>();
services.AddTransient<BetaViewModel>();
services.AddElegantSeriesFlowViews(views => views.RegisterAttributedViews());

using var provider = services.BuildServiceProvider();
var navigation = provider.GetRequiredService<INavigationService>();
var locator = provider.GetRequiredService<IViewLocator>();

// Core navigation against the packaged core assembly.
Check(await navigation.NavigateToAsync<AlphaViewModel>(), "navigate to Alpha");
var alpha = navigation.GetCurrentViewModel();
Check(alpha is AlphaViewModel, "current is AlphaViewModel");

// The source generator (shipped inside the core package as an
// analyzer) turned the [ViewFor] attributes into registrations:
// the packaged Avalonia locator resolves the right view type.
var alphaView = locator.CreateView(alpha!);
Check(alphaView is AlphaView, "generated registration resolves AlphaView for AlphaViewModel");

// The Mvvm package's BaseViewModel works as a navigation page.
Check(await navigation.NavigateToAsync<BetaViewModel>(), "navigate to Beta");
var beta = navigation.GetCurrentViewModel();
Check(beta is BetaViewModel, "current is BetaViewModel (Mvvm BaseViewModel)");
Check(locator.CreateView(beta!) is BetaView, "generated registration resolves BetaView for BetaViewModel");

Check(await navigation.GoBackAsync(), "go back");
Check(ReferenceEquals(alpha, navigation.GetCurrentViewModel()), "back lands on the same Alpha instance");

// KeepAlive: the Beta instance survives being popped and is reused.
Check(await navigation.NavigateToAsync<BetaViewModel>(retention: NavigationRetention.KeepAlive), "navigate to Beta (KeepAlive)");
var keptBeta = navigation.GetCurrentViewModel();
Check(await navigation.GoBackAsync(), "go back from kept Beta");
Check(await navigation.NavigateToAsync<BetaViewModel>(retention: NavigationRetention.KeepAlive), "navigate to Beta again");
Check(ReferenceEquals(keptBeta, navigation.GetCurrentViewModel()), "KeepAlive reuses the cached Beta instance");

Console.WriteLine(failures == 0 ? "CONSUMER PASS" : $"CONSUMER FAIL ({failures} checks failed)");
return failures == 0 ? 0 : 1;

/// <summary>Page on the core <see cref="NavigationViewModelBase"/>.</summary>
public sealed class AlphaViewModel : NavigationViewModelBase
{
}

/// <summary>Page on the Mvvm package's <see cref="ElegantSeries.Flow.Mvvm.BaseViewModel"/>:
/// proves the Mvvm package's base class satisfies the navigation contracts.</summary>
public sealed class BetaViewModel : ElegantSeries.Flow.Mvvm.BaseViewModel
{
}

/// <summary>View for <see cref="AlphaViewModel"/>, declared for the
/// source generator via <see cref="ViewForAttribute"/> (ViewOnly: the
/// ViewModel's DI registration above stays manual).</summary>
[ViewFor(Lifetime = ViewModelLifetime.ViewOnly)]
public sealed class AlphaView : BaseView<AlphaViewModel>
{
}

/// <summary>View for <see cref="BetaViewModel"/>.</summary>
[ViewFor(Lifetime = ViewModelLifetime.ViewOnly)]
public sealed class BetaView : BaseView<BetaViewModel>
{
}
