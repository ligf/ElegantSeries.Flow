# ElegantSeries.Flow.Avalonia

Avalonia UI adapter for [ElegantSeries.Flow](https://github.com/ligf/ElegantSeries.Flow)
(a lightweight, AOT-friendly MVVM navigation library for .NET).

It contains no navigation logic itself — it binds the platform-agnostic
`INavigationService` (core) to Avalonia views:

| Type | Purpose |
|---|---|
| `IViewLocator` / `ViewLocator` | Explicit, zero-reflection ViewModel → View registration |
| `BaseView<TViewModel>` | Typed `UserControl` base class with a `ViewModel` accessor and an `OnViewModelChanged` hook |
| `NavigationHost` | `ContentControl` that shows the active page of a navigation region |
| `IDispatcher` / `AvaloniaDispatcher` | UI-thread marshalling abstraction (testable) |
| `FlowViewServiceExtensions` | `services.AddFlowViews(...)` DI registration |

Requires `ElegantSeries.Flow` **>= 0.1.0** (this package is version 0.1.0).

## Usage

### 1. Register services and views

```csharp
// App startup (e.g. App.axaml.cs / composition root)
services.AddSingleton<INavigationService, NavigationService>(); // core
services.AddTransient<HomeViewModel>();
services.AddTransient<SettingsViewModel>();

services.AddFlowViews(locator =>
{
    locator.Register<HomeView, HomeViewModel>();
    locator.Register<SettingsView, SettingsViewModel>();
    // Views needing constructor arguments:
    // locator.Register<DashboardViewModel>(vm => new DashboardView(new DashboardTheme()));
});
```

### 2. Place a host in AXAML

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:flow="clr-namespace:ElegantSeries.Flow.Avalonia;assembly=ElegantSeries.Flow.Avalonia">
    <flow:NavigationHost RegionName="MainRegion"
                         NavigationService="{Binding Navigation}"
                         ViewLocator="{Binding ViewLocator}" />
</Window>
```

`RegionName` defaults to `"MainRegion"`. `NavigationService` and `ViewLocator` can
also be assigned in code-behind; both are styled properties, so bindings work.

### 3. Write a view

```csharp
public partial class HomeView : BaseView<HomeViewModel>
{
    public HomeView()
    {
        InitializeComponent();
    }

    protected override void OnViewModelChanged(HomeViewModel? oldValue, HomeViewModel? newValue)
    {
        // View-side init that needs the ViewModel (e.g. subscribe to its events).
        // Unsubscribe from oldValue when it is replaced.
    }
}
```

Keep the constructor parameterless (AXAML requirement); all dependencies go
through the ViewModel, which is created by DI inside a per-page scope (core v2.0).

### 4. Navigate (from any ViewModel)

```csharp
await NavigateToAsync<SettingsViewModel>();          // inherited from BaseViewModel
await GoBackAsync();
```

## Threading model

- The core service may raise `RegionNavigated` on any thread (library code uses
  `ConfigureAwait(false)`). `NavigationHost` marshals every UI operation to the
  UI thread via `IDispatcher` (`AvaloniaDispatcher` in production).
- `IViewLocator.Register` is thread-safe, but register once at startup on a
  single thread.
- View factories run on the UI thread and must not block.

## View lifetime

Views are cached in a `ConditionalWeakTable` keyed by ViewModel instance: a view
lives exactly as long as its ViewModel. A `KeepAlive` ViewModel automatically
reuses its view when you navigate back to it; when the navigation service releases
a ViewModel, its view becomes garbage-collectable with no manual cleanup.

If a view factory throws, the host keeps showing the previous page — a failing
factory can never leave the host in a half-updated state.

The host unsubscribes from `INavigationService.RegionNavigated` when detached
from the visual tree and on `Dispose()`, so a discarded host is never kept alive
by the (typically singleton) service.

## AOT / trimming

The package is `IsAotCompatible=true`. View resolution uses only generic type
parameters and a dictionary keyed by `viewModel.GetType()` — no
`Activator.CreateInstance`, no runtime reflection, no source generators.

## Version correspondence

| This package | Requires |
|---|---|
| 0.1.0 | ElegantSeries.Flow >= 0.1.0, Avalonia 12.x |

## License

MIT — see the [repository](https://github.com/ligf/ElegantSeries.Flow).
