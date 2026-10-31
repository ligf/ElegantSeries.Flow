# ElegantSeries.Flow

English | [中文](README.zh-CN.md)

[![NuGet](https://img.shields.io/nuget/v/ElegantSeries.Flow.svg)](https://www.nuget.org/packages/ElegantSeries.Flow)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)

A lightweight, **AOT-friendly** MVVM navigation library for .NET.

ElegantSeries.Flow gives you region-based navigation stacks, an opt-in KeepAlive ViewModel cache,
strongly-typed navigation parameters, navigation guards, and ViewModel lifecycle callbacks —
with no dependency on any UI framework. It targets `net10.0`, is trim/AOT compatible, and is
thread-safe by design.

## Features

- **Region-based navigation** — independent navigation stacks per region (`"MainRegion"` by default)
- **Navigation operations & retention** — `Push` / `Replace` / `Reset`, composed with `Destroy` / `KeepAlive` retention
- **KeepAlive cache** — opt-in ViewModel reuse across navigations
- **Multi-instance recognition** — several instances of one page type coexist; a forward
  navigation finds the right one by parameter, or by the instance itself (`INavigationTargetAware`)
- **Typed parameters** — `NavigateToAsync<TViewModel, TParam>(param)` with compile-time type safety
- **Lifecycle callbacks** — `INavigationAware` (`OnNavigatedTo` / `OnNavigatedFrom`),
  or the async `INavigationAwareAsync` when the transition needs `await`
- **Navigation guards** — `INavigationGuard.CanNavigateFromAsync(cancellationToken)` can cancel navigation,
  or `INavigationGuardWithContext` for target/operation/parameter-aware decisions
- **Cancellation** — `CancellationToken` on navigation calls, honored until the stack updates
- **Refresh** — re-invoke the active page's callbacks with a new parameter (`refreshIfActive`)
- **AOT / trimming safe** — `DynamicallyAccessedMembers` annotations, no runtime reflection
- **Thread-safe** — transitions are serialized; all shared state is lock-guarded
- **Best-effort disposal** — one failing `Dispose` never leaks the remaining ViewModels
  (exceptions are collected and rethrown: single as-is, multiple as `AggregateException`)

## Install

```bash
dotnet add package ElegantSeries.Flow
```

Requires **.NET 10** or later.

## Quick start

### 1. Register the service

```csharp
using ElegantSeries.Flow.Extensions;

services.AddElegantSeriesFlow();   // singleton — single-window apps
// or
services.AddElegantSeriesFlow(ServiceLifetime.Scoped);      // scoped — each window gets its own navigation stack
```

**Singleton or scoped?** A *region* is a named navigation slot — typically one
`NavigationHost` control in your UI — and each region owns an independent page
stack (see [Regions](#regions)). This choice only decides the *scope of those
stacks*:

- `AddElegantSeriesFlow()` registers one app-wide `INavigationService`. Every window
  shares the same region stacks: two windows each hosting a `"MainRegion"` would
  interfere with each other.
- `AddElegantSeriesFlow(ServiceLifetime.Scoped)` gives each DI scope (typically one per window) its
  own `INavigationService` with fully isolated region stacks.

`AddElegantSeriesFlow` uses `TryAdd` internally, so calling it twice registers only the first
one — a single container holds exactly one `INavigationService` registration. The
ViewModels' lifetimes are independent of this choice (see
[Page-level service scopes](#page-level-service-scopes)). Passing
`ServiceLifetime.Transient` throws `ArgumentOutOfRangeException`: the navigation
service is stateful, and a transient registration would hand each consumer an
isolated, empty stack.

**Mixed setup** — one global stack plus isolated windows: register the singleton
for the shared stack, and construct per-window services manually from each
window's scope (they are not registered in DI). The scope must live as long as
the window — dispose it when the window closes, not earlier:

```csharp
services.AddElegantSeriesFlow(); // global stack

// Per-window isolated stack:
var windowScope = rootProvider.CreateScope(); // dispose when the window closes
var windowNavigation = new NavigationService(windowScope.ServiceProvider);
```

### 2. Write a ViewModel

```csharp
using ElegantSeries.Flow.Mvvm;
using ElegantSeries.Flow.Navigation;

public partial class HomeViewModel : BaseViewModel, INavigationAware
{
    public void OnNavigatedTo(object? parameter) { /* page appeared */ }
    public void OnNavigatedFrom() { /* page is leaving */ }

    public Task GoToDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("hello");
}
```

`BaseViewModel.Navigation` is attached automatically when the ViewModel becomes the active
page and cleared when it is navigated away from, so the `protected NavigateToAsync` /
`GoBackAsync` helpers can be called directly from the ViewModel.

`BaseViewModel` lives in the optional `ElegantSeries.Flow.Mvvm` package and builds on
CommunityToolkit.Mvvm. If you don't want the toolkit dependency, derive from
`NavigationViewModelBase` (in the core `ElegantSeries.Flow` package) instead — same
navigation plumbing with a plain `INotifyPropertyChanged` implementation.

Register your ViewModels with DI (transient is the typical lifetime):

```csharp
services.AddTransient<HomeViewModel>();
services.AddTransient<DetailViewModel>();
```

### 3. Navigate

```csharp
var navigation = serviceProvider.GetRequiredService<INavigationService>();

await navigation.NavigateToAsync<HomeViewModel>();                              // push a new page
await navigation.NavigateToAsync<DetailViewModel, string>("hello");             // with a typed parameter
await navigation.NavigateToAsync<DetailViewModel>("Sidebar", operation: NavigationOperation.Replace);
await navigation.GoBackAsync();                                                // pop

bool canGoBack = navigation.CanGoBack();
var current = navigation.GetCurrentViewModel();
```

### 4. Render the active ViewModel

ElegantSeries.Flow is UI-agnostic: it manages ViewModels, and your platform layer maps the
active ViewModel to a View. The typical pattern is a `ContentControl`-style host bound to
`GetCurrentViewModel()` and refreshed on the `RegionNavigated` event. For AOT-safe
ViewModel→View mapping, register views explicitly via `views.Register` /
`views.RegisterTransient` / `views.RegisterSingleton` inside `services.AddElegantSeriesFlowViews(...)`
— no runtime reflection, no naming conventions. Alternatively, put a `[ViewFor]`
attribute (`ElegantSeries.Flow.Routing`) on the View class — the ViewModel is
inferred from the View's `BaseView<TViewModel>` base class, or named explicitly
as `[ViewFor<TViewModel>]` for Views without that base —
and let the ElegantSeries.Flow source generator emit the same registrations into a
generated `RegisterAttributedViews()` extension (call it inside `AddElegantSeriesFlowViews`;
the generated code lives in your own assembly's `<assembly name>.g`
namespace — add a `using` for it); the
attribute's `Lifetime` selects `Transient` (default), `Singleton`, or `ViewOnly`
(mapping only — register the ViewModel in DI yourself). The runtime never scans the
attribute. Register a ViewModel either manually or via the attribute, not both:
duplicates throw `InvalidOperationException` at startup.)

## Packages

| Package | Contents |
|---------|----------|
| `ElegantSeries.Flow` | Core: navigation service, regions, page DI scopes, KeepAlive cache, guards, lifecycle — no MVVM toolkit dependency |
| `ElegantSeries.Flow.Mvvm` | Optional CommunityToolkit.Mvvm integration: `BaseViewModel` (`ElegantSeries.Flow.Mvvm`) |
| `ElegantSeries.Flow.WPF` | WPF host: `NavigationHost`, `BaseView<TViewModel>`, `ViewLocator` |
| `ElegantSeries.Flow.Avalonia` | Avalonia host: `NavigationHost`, `BaseView<TViewModel>`, `ViewLocator` |
| `ElegantSeries.Flow.Generator` | Roslyn source generator: emits `IViewLocator` registrations from `[ViewFor]` attributes (Analyzer-only, no runtime dependency) |
| `ElegantSeries.Flow.Diagnostics` | Optional metrics observer: turns navigation events into counters on the `ElegantSeries.Flow.Diagnostics` meter (`navigation.completed`, `viewmodel.released`, `cache.cleared`, `subscriber.errors`) for MeterListener / OpenTelemetry |
| `ElegantSeries.Flow.Routing` | Optional URI routing: an explicit `RouteTableBuilder` maps URI patterns (`orders/{id}`) to typed navigations — registration order is match priority, no reflection, AOT-safe |

## UI integration

Ready-made hosts for desktop UI frameworks — same API surface, per-platform namespaces:

| Package | Targets | Host control | View base class |
|---------|---------|--------------|-----------------|
| `ElegantSeries.Flow.WPF` | `net10.0-windows` | `NavigationHost` (`ElegantSeries.Flow.WPF.Hosting`) | `BaseView<TViewModel>` (`ElegantSeries.Flow.WPF.Views`) |
| `ElegantSeries.Flow.Avalonia` | `net10.0` | `NavigationHost` (`ElegantSeries.Flow.Avalonia.Hosting`) | `BaseView<TViewModel>` (`ElegantSeries.Flow.Avalonia.Views`) |

```bash
dotnet add package ElegantSeries.Flow.WPF        # WPF apps
dotnet add package ElegantSeries.Flow.Avalonia   # Avalonia apps
```

```csharp
// 1. Register views (AOT-safe: no runtime reflection)
services.AddElegantSeriesFlowViews(locator =>
{
    locator.RegisterTransient<HomeView, HomeViewModel>();   // view mapping + AddTransient
    locator.RegisterTransient<DetailView, DetailViewModel>();
    // Or map only and register the ViewModel in DI yourself:
    // locator.Register<HomeView, HomeViewModel>();
});

// 2. Provide the host with its dependencies — via XAML binding (MVVM):
//    <flow:NavigationHost RegionName="MainRegion"
//                          NavigationService="{Binding NavigationService}"
//                          ViewLocator="{Binding ViewLocator}" />
//    or in code-behind:
//    navigationHost.NavigationService = navigationService;
//    navigationHost.ViewLocator = viewLocator;
//    The host shows the region's current page immediately; navigation order
//    does not matter.
```

```xml
<!-- 3. Drop the host in XAML (WPF shown; Avalonia uses the ...Avalonia.Hosting namespace) -->
<Window xmlns:flow="clr-namespace:ElegantSeries.Flow.WPF.Hosting;assembly=ElegantSeries.Flow.WPF">
    <flow:NavigationHost x:Name="navigationHost" RegionName="MainRegion" />
</Window>
```

Both hosts listen to `RegionNavigated`, resolve the View via the registered `IViewLocator`
(`ElegantSeries.Flow.WPF.Locating` / `ElegantSeries.Flow.Avalonia.Locating`), marshal to the
UI thread through `IDispatcher` (`...Threading`), and set it as content. ViewModels must
implement `INavigationViewModel` (or derive from `BaseViewModel` / `NavigationViewModelBase`).

See [ElegantSeries.Flow.WPF](src/ElegantSeries.Flow.WPF/README.md) and
[ElegantSeries.Flow.Avalonia](src/ElegantSeries.Flow.Avalonia/README.md) for full usage,
and [samples](samples/) for runnable WPF/Avalonia demos (multi-region layout,
typed parameters, KeepAlive, singleton ViewModel).

### View factories

When a view needs constructor arguments, or its construction depends on where the
page is shown, register a factory instead of a view type:

```csharp
services.AddElegantSeriesFlowViews(locator =>
{
    // Simple factory: receives the ViewModel only.
    locator.Register<ChartViewModel>(vm => new ChartView(vm));

    // Context-aware factory: also receives the region and the parameter the
    // page was most recently activated with.
    locator.RegisterFactory<DetailViewModel>(context =>
        context.RegionName == "Sidebar"
            ? new DetailSidebarView()
            : new DetailView());
});
```

The context-aware factory receives a `ViewCreationContext`
(`ElegantSeries.Flow.Locating`): the ViewModel instance, the region name
(`null` only when a view is created through the legacy `CreateView(viewModel)`
path), and the parameter the page was most recently activated with. The context
deliberately carries no `IServiceProvider` and no page scope — a page's scope is
disposed when the page leaves, so a view that needs services should capture the
application's root provider in the factory closure, or receive them through its
ViewModel.

Factories run on the UI thread, once per ViewModel instance: the host caches the
created view per instance, so a factory decides which view a page gets when it
is first created. It is not consulted again when a kept-alive page is
re-activated, and cannot swap the view at that point.

## Navigation operations and retention

Every navigation composes two independent choices: a `NavigationOperation`
(what happens to the stack) and a `NavigationRetention` (what happens to the
new page when it later leaves).

| Operation | Behavior |
|------|----------|
| `Push` | Push a new page onto the region stack. The previous page is deactivated. |
| `Replace` | Replace the current page. The old page leaves per its own retention. |
| `Reset` | Drop the whole stack and start fresh with the new page. |

| Retention | Behavior |
|------|----------|
| `Destroy` | The page is destroyed when it leaves (its page scope is disposed). |
| `KeepAlive` | The page is cached when it leaves; a later `KeepAlive` navigation to the same type reuses the cached ViewModel when the navigation is recognized as addressed to it (see *Recognizing an existing page*), instead of creating a new one. |

## Regions

A *region* is a named navigation slot — usually one `NavigationHost` control —
and each region owns an independent page stack. Navigating in one region never
affects another, so different parts of a window can navigate independently:

```xml
<!-- Sidebar region: menu pages -->
<flow:NavigationHost RegionName="Sidebar" />
<!-- Main region: detail pages -->
<flow:NavigationHost RegionName="MainRegion" />
```

```csharp
await navigation.NavigateToAsync<MenuViewModel>("Sidebar");
await navigation.NavigateToAsync<DetailViewModel>("MainRegion");
await navigation.GoBackAsync("Sidebar");   // only the sidebar pops
```

The default region name is `"MainRegion"`, so single-region apps can omit the
argument everywhere. A ViewModel *instance* can only be active in one region at
a time — navigating an instance that already lives on a *different* region's
stack throws `InvalidOperationException`.

## Lifecycle

```csharp
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);  // strongly-typed via INavigationAware<T>
    void OnNavigatedFrom();
}

public interface INavigationAwareAsync   // implement INSTEAD of INavigationAware when you need await
{
    Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default);
    Task OnNavigatedFromAsync(CancellationToken cancellationToken = default);
}

public interface INavigationGuard
{
    Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken);  // return false to cancel navigation
}

public interface INavigationGuardWithContext  // instead of INavigationGuard, when the decision needs context
{
    Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken);
    // context: RegionName, TargetViewModelType, Operation (null for back), Retention (null for back), Parameter, IsBack
}

public interface INavigationTargetAware  // optional: claim or refuse a navigation addressed to your page type
{
    bool IsNavigationTarget(object? parameter);
}
```

Lifecycle callbacks and page-scope disposals always run **outside** internal locks, so they may
safely call back into `INavigationService` without deadlocking. The fixed transition order
is: `OnNavigatedFrom` → page-scope disposal → `OnNavigatedTo` → events (`RegionNavigated`).
UI layers should swap views on `RegionNavigated`; the view is not guaranteed to be attached
yet when `OnNavigatedTo` runs.

A callback that throws does not abort the transition: the exception is collected and
rethrown afterwards (single as-is, several as `AggregateException`). The token passed to
`INavigationAwareAsync` callbacks is cancelled when the page leaves the active position
(covered, popped, replaced, or cleared) — even if the callback is still running. Honor
the token: stop touching the page's dependencies once it fires. An `OperationCanceledException`
caused by this lifecycle cancellation is treated as normal control flow, not an error.

### Recognizing an existing page

Several instances of the same page type can coexist — two order details, two
documents. When a forward navigation's target type already has `KeepAlive`
instances (covered on the stack, or held in the KeepAlive cache), the
navigation is addressed to exactly one of them. Candidates are considered
nearest to the stack top first, and each candidate is recognized in one of
two ways:

- **By parameter.** The parameter the candidate was last activated with is
  compared to the incoming parameter with `object.Equals`. Recognition
  therefore works with no code at all when the parameter is a value type, a
  `string`, or a `record` — pass the id itself, or a small record:

  ```csharp
  await navigation.NavigateToAsync<OrderDetailViewModel, int>(42, retention: NavigationRetention.KeepAlive);
  await navigation.NavigateToAsync<OrderDetailViewModel, int>(43, retention: NavigationRetention.KeepAlive); // a second instance
  await navigation.NavigateToAsync<OrderDetailViewModel, int>(42, retention: NavigationRetention.KeepAlive); // pops back to the first one
  ```

- **By the instance itself.** A ViewModel can implement
  `INavigationTargetAware.IsNavigationTarget(parameter)` to claim — or
  refuse — a navigation. Its verdict is final for that candidate. Use it
  when identity lives in the instance's current state rather than in the
  stored parameter, e.g. a document whose file was renamed through Save As
  after it was opened. Recognition is not serialized with page disposal:
  a candidate's page scope may be disposed concurrently on another thread
  (a concurrent `Remove`, cache clear, or service disposal) while the
  method runs, so it must not rely on services captured from its page
  scope still being alive — decide from the instance's own state.

If no candidate is recognized, a new instance is created. An existing
instance is never reused just because it has the right type — that is what
keeps order 43 from being delivered to order 42's page. A parameter whose
type has no value equality (a plain class without an `Equals` override)
can never be recognized by parameter; the first time that costs a reuse,
the service reports it once per parameter type via `SubscriberError`.
Remedies, cheapest first: pass the id itself, make the parameter a
`record`, or implement `INavigationTargetAware` on the ViewModel (the
parameter type itself does not need to change for the last one).

### Navigation requests issued from callbacks

A page's *window* spans from the start of its `OnNavigatedTo` to the end of its
`OnNavigatedFrom`. Navigation calls made while a window is open — from lifecycle
callbacks, from the leave guard, or from code that runs while a transition is in
flight — do not execute immediately. Each call registers a pending request for the
region, and a later registration replaces an earlier one: when the window closes,
only the **last** registered request executes.

- A request issued from inside a callback returns an already-completed task
  (`true` = registered; `false` if the service has already been disposed and
  the request was discarded); it is never awaited against the transition it belongs to.
- A superseded request has no effect at all: its ViewModel is never constructed
  and no lifecycle callbacks run for it. An outside caller awaiting a superseded
  request receives `false`.
- The executed request constructs its ViewModel *after* the current page's
  `OnNavigatedFrom` has completed, becomes the current page, and opens its own
  window. Redirect chains — a page whose `OnNavigatedTo` navigates onward —
  therefore proceed hop by hop, and each intermediate page runs a complete
  `OnNavigatedTo` → `OnNavigatedFrom` cycle.
- `RegionNavigated` is raised only when a page comes to rest as the region's
  current page; intermediate pages of a redirect chain do not raise it.
- The leave guard runs once per departure, with the request that started the
  departure as its context; a request registered later does not re-run it.
- Consecutive hops driven by callback-registered requests are bounded by
  `NavigationOptions.MaxNavigationChainLength` (default 10). When the limit is
  hit, the chain is stopped and the stop is reported via `SubscriberError` as
  an `InvalidOperationException` — such requests have no awaiting caller, so
  the limit never faults a caller's task — the current page stays active and
  intact, and the service remains usable.

### Cancellation

`NavigateToAsync` / `GoBackAsync` accept a `CancellationToken` that is honored while
the request is still pending: cancelling withdraws the request — it never executes —
and the awaiting caller throws `OperationCanceledException`. Once the current page's
`OnNavigatedFrom` has started for the departure that will execute the request, the
departure is no longer interrupted: the callback runs to completion. A cancellation
that arrives afterwards is still honored where the executing step checks the token
(while the target is being resolved and just before the commit); withdrawing there
re-activates the departing page — its `OnNavigatedTo` runs again — instead of
completing the departure. (Those checkpoints exist on the `NavigateToAsync` hop
only; `GoBackAsync` has none, so once its `OnNavigatedFrom` has started the token
no longer withdraws the request.)

### Refreshing the active page

Navigating to the already-active type with a default request (`Push` + `Destroy`)
is a silent no-op returning `true`. Pass `refreshIfActive: true` to turn it into a
*refresh*: nothing is pushed and no new page scope is created — the active instance's
activation callbacks simply run again with the new parameter, and `RegionNavigated`
is raised. The current page's from-guard is still consulted and may veto the refresh.
An explicit operation or retention (`Replace`, `Reset`, `KeepAlive`) takes precedence
over this shortcut and is always honored.

### Page-level service scopes

Every page gets its own `IServiceScope`. The ViewModel is resolved from that scope, and
leaving the page disposes the scope — the DI container then releases the ViewModel and
its whole Transient/Scoped dependency graph. The navigation service never disposes
ViewModels directly.

How the ViewModel's own DI lifetime — your `services.AddXxx<MyViewModel>()` call,
not the navigation service registration — interacts with the page scope:

| ViewModel lifetime | Navigation behavior | When the page is left |
|---|---|---|
| `Transient` | A new instance per page scope | The scope disposes the ViewModel and its dependency graph |
| `Scoped` | One instance per page scope | The scope disposes it |
| `Singleton` | Shared from the root container | The page scope never disposes it |

A ViewModel *instance* never appears twice on one region's stack: navigating to the
already-active type with a default (`Push` + `Destroy`) request is a no-op returning
`true` (or a *refresh* with `refreshIfActive`,
re-invoking the active instance's callbacks with the new parameter); navigating to a
type whose instance lives deeper on the stack *pops back to it* — the pages above are
dropped and the existing instance is re-activated. Singleton ViewModels and cached
KeepAlive pages therefore survive navigation away and are reused on return, matching
mainstream frameworks (cf. Prism's `IsNavigationTarget`). Only an instance living on a
*different* region's stack throws `InvalidOperationException` (a ViewModel cannot be
active in two regions at once).

`ViewModelReleased` means the service **released ownership** (the page left navigation
state and its scope is being disposed), not "every disposable was released": the DI
container stops a scope at the first throwing disposable. The event also fires for
Singleton ViewModels when their page is torn down — the page scope is disposed, but the
shared instance itself survives because it is owned by the root container.

## KeepAlive cache

`NavigationRetention.KeepAlive` keeps the page (ViewModel + its page scope) in a per-region
cache keyed by `(region, ViewModel type)`. Navigating to the same type reuses the cached
page and its scope — no new scope is created — when the navigation is recognized as
addressed to the cached instance (by parameter equality or by the instance itself, see
*Recognizing an existing page* above); an unrecognized cached page is bypassed and a new
instance is created instead.

```csharp
await navigation.NavigateToAsync<SettingsViewModel>(retention: NavigationRetention.KeepAlive);

navigation.ClearCache("MainRegion");   // or await navigation.ClearCacheAsync("MainRegion");
navigation.ClearAllCache();            // or await navigation.ClearAllCacheAsync();
```

One `(region, type)` key holds at most one cached page. Page scopes still referenced by a
navigation stack are **not** disposed immediately; their disposal is deferred until the
last stack/cache reference disappears. Disposal prefers `IAsyncDisposable` on the async
paths. The synchronous paths dispose scopes via `IDisposable.Dispose` — if a page scope
contains a service that only implements `IAsyncDisposable`, the DI container throws
`InvalidOperationException` (use the async variants when pages need async cleanup).

### Disposal error semantics

Teardown is **best-effort across pages**: every page scope is attempted even if one of
them throws, so a single faulty `Dispose` can never leak the remaining pages. After
cleanup and events finish, collected exceptions are rethrown — a single exception is
rethrown preserving its original stack trace, several are wrapped in an
`AggregateException`.

**Caveat:** *within* one page scope the DI container stops disposing remaining services
after the first throwing disposable (platform behavior, sync and async alike). Only
cross-page isolation is provided by the navigation service.

**Best practice:** keep each disposable's `Dispose` idempotent and non-throwing where
possible; if a page owns several critical resources that must all be released, coordinate
their teardown in the ViewModel (e.g. `OnNavigatedFromAsync` or the ViewModel's own
`Dispose`) rather than relying on container order.

**Best practice:** prefer `DisposeAsync` when pages use `INavigationAwareAsync`.
Synchronous `Dispose` cancels lifecycle tokens but does not wait for the callbacks to
finish before scopes are released; `DisposeAsync` waits for them (bounded by the
disposal-gate timeout) — and concurrent `DisposeAsync` calls join the same disposal,
so awaiting any of them means the cleanup has finished, unless a synchronous
`Dispose` races it: the synchronous path never joins, takes over with its weaker
guarantee, and the asynchronous operation then completes without waiting for it.

## Thread safety

All public members are safe to call from any thread. Navigation requests within a
region are executed one at a time by the region's navigation pump; shared state is
guarded by a dedicated lock.

## Native AOT / trimming

The library sets `IsAotCompatible=true` and annotates generic ViewModel parameters with
`[DynamicallyAccessedMembers(PublicConstructors)]`. ViewModels are resolved through
`IServiceProvider` — register them in DI and avoid `Activator.CreateInstance` at the
app layer. The `[ViewFor]` attribute (on the View class, declaring its ViewModel) is
consumed by the ElegantSeries.Flow source generator, which emits the equivalent
`IViewLocator` registrations at compile time; it is not scanned at runtime. Note: the WPF sample app itself
cannot be NativeAOT-published (WPF as a framework does not support Native AOT); the
Avalonia sample has `PublishAot=true` in its csproj as the proof of the
framework's AOT story (verified by the Native AOT publish smoke test in CI).

## API overview

| Member | Description |
|--------|-------------|
| `NavigateToAsync<T>(region?, operation?, retention?, refreshIfActive?, cancellationToken?)` | Navigate to a ViewModel type |
| `NavigateToAsync<T, TParam>(param, region?, operation?, retention?, refreshIfActive?, cancellationToken?)` | Navigate with a typed parameter |
| `GoBackAsync(region?, cancellationToken?)` | Pop the current page |
| `GoBackToAsync<T>(region?, cancellationToken?)` / `GoBackToAsync<T, TParam>(param, region?, cancellationToken?)` | Go back to a page on the stack, popping everything above it |
| `SetRootAsync<T>(region?, retention?, cancellationToken?)` / `SetRootAsync<T, TParam>(param, region?, retention?, cancellationToken?)` | Replace the whole stack with a new root page |
| `Remove<T>(region?)` / `Remove<T, TParam>(param, region?)` | Synchronously remove covered pages from the stack |
| `CanGoBack(region?)` / `GetCurrentViewModel(region?)` / `IsActive<T>(region?)` / `GetCurrentRetention(region?)` / `GetCurrentParameter(viewModel, region?)` / `GetNavigationStack(region?)` | Queries |
| `ClearCache(region)` / `ClearCacheAsync(region)` | Clear one region's KeepAlive cache |
| `ClearAllCache()` / `ClearAllCacheAsync()` | Clear all KeepAlive caches |
| `RegionNavigated` / `ViewModelReleased` / `RegionCacheCleared` / `SubscriberError` | Events |
| `SubscribeRegionNavigated` / `SubscribeViewModelReleased` / `SubscribeRegionCacheCleared` / `SubscribeSubscriberError` | Disposable event subscriptions |
| `AddElegantSeriesFlow()` / `AddElegantSeriesFlow(ServiceLifetime.Scoped)` | DI registration |
| `AddElegantSeriesFlow(configure, lifetime?)` | DI registration with `NavigationOptions` |
| `NavigationOptions.DisposalGateTimeout` | Async lifecycle wait timeout (default 5s) |
| `NavigationOptions.SyncLockTimeout` | Sync API lock acquisition timeout (default 2s) |
| `NavigationOptions.MaxNavigationChainLength` | Max consecutive callback-driven hops (default 10) |

## Contributing

Issues and pull requests are welcome. Please keep new code AOT/trimming compatible
(the test suite and `IsAotCompatible` flag guard this) and add tests for behavior changes.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
