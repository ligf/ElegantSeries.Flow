using System.Diagnostics.Metrics;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Avalonia.Extensions;
using ElegantSeries.Flow.Diagnostics;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Avalonia.Views;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Generated.ConsumerApp;
using ElegantSeries.Flow.Journal;
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

string? capturedRouteName = null;
var services = new ServiceCollection();
services.AddElegantSeriesFlow();
services.AddElegantSeriesFlowDiagnostics();
services.AddElegantSeriesFlowJournal(journal => journal
    .RegisterParameter<string>(value => value, value => value));
services.AddElegantSeriesFlowRouting(routes => routes
    .Map<BetaViewModel>("beta")
    .Map<AlphaViewModel, string>("alpha/{name}", match =>
    {
        capturedRouteName = match.Values["name"];
        return capturedRouteName;
    }));
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

// The generator's remaining lifetime forms, consumed through the
// package: with Lifetime = Singleton / Transient the generated
// RegisterAttributedViews also registers the ViewModel in DI with
// that lifetime (the ViewOnly views above keep their manual DI
// registrations instead), so Gamma and Delta have no manual
// registration in the setup on purpose.
Check(await navigation.NavigateToAsync<GammaViewModel>(), "navigate to Gamma");
var gamma = navigation.GetCurrentViewModel();
Check(gamma is GammaViewModel, "current is GammaViewModel");
Check(locator.CreateView(gamma!) is GammaView, "generated registration resolves GammaView for GammaViewModel");
Check(await navigation.GoBackAsync(), "go back from Gamma");
Check(await navigation.NavigateToAsync<GammaViewModel>(), "navigate to Gamma again");
Check(ReferenceEquals(gamma, navigation.GetCurrentViewModel()), "generated Singleton registration reuses the same GammaViewModel");
Check(await navigation.GoBackAsync(), "go back from Gamma again");

Check(await navigation.NavigateToAsync<DeltaViewModel>(), "navigate to Delta");
var delta = navigation.GetCurrentViewModel();
Check(delta is DeltaViewModel, "current is DeltaViewModel");
Check(locator.CreateView(delta!) is DeltaView, "generated registration resolves DeltaView for DeltaViewModel");
Check(await navigation.GoBackAsync(), "go back from Delta");
Check(await navigation.NavigateToAsync<DeltaViewModel>(), "navigate to Delta again");
Check(!ReferenceEquals(delta, navigation.GetCurrentViewModel()), "generated Transient registration creates a new DeltaViewModel each time");
Check(await navigation.GoBackAsync(), "go back from Delta again");

// The Diagnostics package observes the packaged core's events:
// resolving NavigationDiagnostics starts its subscriptions, and a
// MeterListener on its meter sees the next navigation counted.
var diagnostics = provider.GetRequiredService<NavigationDiagnostics>();
Check(ReferenceEquals(diagnostics, provider.GetRequiredService<NavigationDiagnostics>()), "Diagnostics resolves as a singleton");
long completedNavigations = 0;
using var meterListener = new MeterListener();
meterListener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == NavigationDiagnostics.MeterName && instrument.Name == "navigation.completed")
        listener.EnableMeasurementEvents(instrument);
};
meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => completedNavigations += value);
meterListener.Start();
Check(await navigation.NavigateToAsync<AlphaViewModel>(), "navigate to Alpha (observed)");
Check(completedNavigations == 1, "Diagnostics counted the completed navigation");

// The Routing package resolves its router from the same container and
// navigates by URI through the packaged core.
var router = provider.GetRequiredService<IRouter>();
Check(await router.NavigateAsync("beta"), "Routing navigates to Beta by URI");
Check(navigation.GetCurrentViewModel() is BetaViewModel, "Routing landed on Beta");
Check(await router.NavigateAsync("alpha/consumer"), "Routing placeholder route navigates");
Check(capturedRouteName == "consumer", "Routing parameter factory received the placeholder value");
Check(!await router.TryNavigateAsync("no/such/route"), "Routing TryNavigate returns false for an unknown URI");

// The Journal package's decorator wrapped the packaged core's
// navigation service, so every navigation above was recorded; the
// routed string parameter was frozen with the registered serializer.
var journal = provider.GetRequiredService<INavigationJournal>();
var journalEntries = await journal.GetEntriesAsync();
Check(journalEntries.Count > 0, "Journal recorded the navigations above");
Check(journalEntries.Any(e => e.ParameterSnapshot == "consumer"), "Journal froze the route parameter snapshot");
var journalSnapshot = await journal.CaptureSnapshotAsync();
Check(journalSnapshot.Regions.Count > 0, "Journal captured a stack snapshot");
var restoredPages = 0;
var restoreResult = await journal.RestoreAsync(journalSnapshot, _ =>
{
    restoredPages++;
    return Task.FromResult(true);
});
Check(restoreResult.AllSucceeded && restoredPages == journalSnapshot.Regions.Sum(r => r.Pages.Count),
    "Journal restore walked every snapshot page");

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

/// <summary>Page whose ViewModel registration is generated with the
/// Singleton lifetime (no manual DI registration in the setup).</summary>
public sealed class GammaViewModel : NavigationViewModelBase
{
}

/// <summary>Page whose ViewModel registration is generated with the
/// Transient lifetime (no manual DI registration in the setup).</summary>
public sealed class DeltaViewModel : NavigationViewModelBase
{
}

/// <summary>View for <see cref="GammaViewModel"/>; the generator
/// emits the view mapping plus a Singleton ViewModel registration.</summary>
[ViewFor(Lifetime = ViewModelLifetime.Singleton)]
public sealed class GammaView : BaseView<GammaViewModel>
{
}

/// <summary>View for <see cref="DeltaViewModel"/>; the generator
/// emits the view mapping plus a Transient ViewModel registration.</summary>
[ViewFor(Lifetime = ViewModelLifetime.Transient)]
public sealed class DeltaView : BaseView<DeltaViewModel>
{
}
