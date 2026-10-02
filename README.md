# ElegantSeries.Flow

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
- **Navigation modes** — `New`, `Replace`, `KeepAlive`, `ClearStack`
- **KeepAlive cache** — opt-in ViewModel reuse across navigations
- **Typed parameters** — `NavigateToAsync<TViewModel, TParam>(param)` with compile-time type safety
- **Lifecycle callbacks** — `INavigationAware` (`OnNavigatedTo` / `OnNavigatedFrom`),
  or the async `INavigationAwareAsync` when the transition needs `await`
- **Navigation guards** — `INavigationGuard.CanNavigateFromAsync()` can cancel navigation,
  or `INavigationGuardWithContext` for target/mode/parameter-aware decisions
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
using ElegantSeries.Flow.Core.Extensions;

services.AddFlowNavigation();          // singleton — single-window apps
// or
services.AddScopedFlowNavigation();    // scoped — each window gets its own navigation stack
```

### 2. Write a ViewModel

```csharp
using ElegantSeries.Flow.Core.ViewModels;
using ElegantSeries.Flow.Core.Navigation;

public partial class HomeViewModel : BaseViewModel, INavigationAware
{
    public void OnNavigatedTo(object? parameter) { /* page appeared */ }
    public void OnNavigatedFrom() { /* page is leaving */ }

    public Task GoToDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("hello", mode: NavigationMode.New);
}
```

`BaseViewModel.Navigation` is attached automatically when the ViewModel becomes the active
page and cleared when it is navigated away from, so the `protected NavigateToAsync` /
`GoBackAsync` helpers can be called directly from the ViewModel.

`BaseViewModel` builds on CommunityToolkit.Mvvm. If you don't want the toolkit
dependency, derive from `NavigationViewModelBase` instead — same navigation plumbing
with a plain `INotifyPropertyChanged` implementation.

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
await navigation.NavigateToAsync<DetailViewModel>("Sidebar", NavigationMode.Replace);
await navigation.GoBackAsync();                                                // pop

bool canGoBack = navigation.CanGoBack();
var current = navigation.GetCurrentViewModel();
```

### 4. Render the active ViewModel

ElegantSeries.Flow is UI-agnostic: it manages ViewModels, and your platform layer maps the
active ViewModel to a View. The typical pattern is a `ContentControl`-style host bound to
`GetCurrentViewModel()` and refreshed on the `RegionNavigated` event. For AOT-safe
ViewModel→View mapping, decorate ViewModels with `[AotRoute(typeof(DetailView))]` from
the `ElegantSeries.Flow.Core.Routing` namespace.

## Navigation modes

| Mode | Behavior |
|------|----------|
| `New` | Push a new page onto the region stack. The previous page is deactivated. |
| `Replace` | Replace the current page. The old page is disposed unless it was `KeepAlive`. |
| `KeepAlive` | Reuse a cached ViewModel of the same type in the region instead of creating a new one. |
| `ClearStack` | Drop the whole stack and start fresh with the new page. |

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
    Task<bool> CanNavigateFromAsync();      // return false to cancel navigation
}

public interface INavigationGuardWithContext  // instead of INavigationGuard, when the decision needs context
{
    Task<bool> CanNavigateFromAsync(NavigationGuardContext context);
    // context: RegionName, TargetViewModelType, Mode (null for back), Parameter, IsBack
}
```

Lifecycle callbacks and page-scope disposals always run **outside** internal locks, so they may
safely call back into `INavigationService` without deadlocking. The fixed transition order
is: `OnNavigatedFrom` → page-scope disposal → `OnNavigatedTo` → events (`RegionNavigated`).
UI layers should swap views on `RegionNavigated`; the view is not guaranteed to be attached
yet when `OnNavigatedTo` runs.

A callback that throws does not abort the transition: the exception is collected and
rethrown afterwards (single as-is, several as `AggregateException`). The token passed to
`INavigationAwareAsync` callbacks is always `CancellationToken.None` — once the stack has
been updated the transition runs to completion.

### Cancellation

`NavigateToAsync` / `GoBackAsync` accept a `CancellationToken` that is honored **until the
region stack is updated**: cancellation before that point releases the created page scope
(if any) and throws `OperationCanceledException`. Afterwards the transition runs to
completion and the token is ignored.

### Refreshing the active page

Navigating to the already-active type is a silent no-op returning `true`. Pass
`refreshIfActive: true` to turn it into a *refresh*: nothing is pushed and no new page
scope is created — the active instance's activation callbacks simply run again with the
new parameter, and `RegionNavigated` is raised. The current page's from-guard is still
consulted and may veto the refresh.

### Page-level service scopes (v2.0)

Every page gets its own `IServiceScope`. The ViewModel is resolved from that scope, and
leaving the page disposes the scope — the DI container then releases the ViewModel and
its whole Transient/Scoped dependency graph. The navigation service never disposes
ViewModels directly.

| Registration | Navigation behavior | When the page is left |
|---|---|---|
| `Transient` | A new instance per page scope | The scope disposes the ViewModel and its dependency graph |
| `Scoped` | One instance per page scope | The scope disposes it |
| `Singleton` | Shared from the root container | The page scope never disposes it |

A ViewModel *instance* never appears twice on one region's stack: navigating to the
already-active type is a no-op returning `true` (or a *refresh* with `refreshIfActive`,
re-invoking the active instance's callbacks with the new parameter); navigating to a
type whose instance lives deeper on the stack *pops back to it* — the pages above are
dropped and the existing instance is re-activated. Singleton ViewModels and cached
KeepAlive pages therefore survive navigation away and are reused on return, matching
mainstream frameworks (cf. Prism's `IsNavigationTarget`). Only an instance living on a
*different* region's stack throws `InvalidOperationException` (a ViewModel cannot be
active in two regions at once).

`ViewModelDisposed` means the service **released ownership** (the page left navigation
state and its scope is being disposed), not "every disposable was released": the DI
container stops a scope at the first throwing disposable. The event also fires for
Singleton ViewModels when their page is torn down — the page scope is disposed, but the
shared instance itself survives because it is owned by the root container.

## KeepAlive cache

`NavigationMode.KeepAlive` keeps the page (ViewModel + its page scope) in a per-region
cache keyed by `(region, ViewModel type)`. Navigating to the same type reuses the cached
page and its scope — no new scope is created.

```csharp
await navigation.NavigateToAsync<SettingsViewModel>(mode: NavigationMode.KeepAlive);

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

## Thread safety

All public members are safe to call from any thread. Navigation transitions and disposal
are serialized with an async lock; shared state is guarded by a dedicated lock.

## Native AOT / trimming

The library sets `IsAotCompatible=true` and annotates generic ViewModel parameters with
`[DynamicallyAccessedMembers(PublicConstructors)]`. ViewModels are resolved through
`IServiceProvider` — register them in DI and avoid `Activator.CreateInstance` at the
app layer. The `[AotRoute]` attribute exists for source-generator-based View resolution
in platform-specific layers.

## API overview

| Member | Description |
|--------|-------------|
| `NavigateToAsync<T>(region?, mode?, refreshIfActive?, cancellationToken?)` | Navigate to a ViewModel type |
| `NavigateToAsync<T, TParam>(param, region?, mode?, refreshIfActive?, cancellationToken?)` | Navigate with a typed parameter |
| `GoBackAsync(region?, cancellationToken?)` | Pop the current page |
| `CanGoBack(region?)` / `GetCurrentViewModel(region?)` / `IsActive<T>(region?)` / `GetCurrentMode(region?)` | Queries |
| `ClearCache(region?)` / `ClearCacheAsync(region?)` | Clear one region's KeepAlive cache |
| `ClearAllCache()` / `ClearAllCacheAsync()` | Clear all KeepAlive caches |
| `RegionNavigated` / `ViewModelDisposed` / `RegionCacheCleared` | Events |
| `AddFlowNavigation()` / `AddScopedFlowNavigation()` | DI registration |

## Contributing

Issues and pull requests are welcome. Please keep new code AOT/trimming compatible
(the test suite and `IsAotCompatible` flag guard this) and add tests for behavior changes.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
