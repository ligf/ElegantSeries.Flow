# ElegantSeries.Flow.WPF

WPF UI integration for the [ElegantSeries.Flow](https://github.com/ligf/ElegantSeries.Flow)
MVVM navigation library: an AOT-friendly view locator, a typed `BaseView` base
class, and a region-aware `NavigationHost` control.

Requires **ElegantSeries.Flow (core) >= 0.1.0**.

## Setup

```csharp
var services = new ServiceCollection();

// Core navigation service (singleton).
services.AddSingleton<INavigationService>(sp =>
    new NavigationService(sp));

// WPF view infrastructure: singleton IViewLocator with explicit registrations.
services.AddFlowViews(views =>
{
    // View mapping + ViewModel DI registration in one call:
    views.RegisterTransient<OrderView, OrderViewModel>();
    views.RegisterSingleton<MenuView, MenuViewModel>();
    // Views needing constructor arguments (mapping only; register the
    // ViewModel in DI yourself):
    views.Register<ReportViewModel>(vm => new ReportView(vm.Title));
});
services.AddTransient<ReportViewModel>();
// ...

var provider = services.BuildServiceProvider();
```

## Hosting a region (XAML)

```xml
<Window xmlns:flow="clr-namespace:ElegantSeries.Flow.WPF.Hosting;assembly=ElegantSeries.Flow.WPF">
    <flow:NavigationHost x:Name="MainHost" RegionName="MainRegion" />
</Window>
```

```csharp
public partial class MainWindow : Window
{
    public MainWindow(INavigationService navigation, IViewLocator views)
    {
        InitializeComponent();
        MainHost.NavigationService = navigation;
        MainHost.ViewLocator = views;
    }

    protected override void OnClosed(EventArgs e)
    {
        MainHost.Dispose(); // unsubscribes from the navigation service
        base.OnClosed(e);
    }
}
```

Then navigate from any ViewModel:

```csharp
await Navigation.NavigateToAsync<OrderViewModel>();
```

The host listens to `INavigationService.RegionNavigated`, creates the registered
view, sets the ViewModel as `DataContext`, and shows it. Views are cached per
ViewModel instance, so KeepAlive navigation reuses the existing view
automatically.

## Writing a view

```xml
<!-- OrderView.xaml -->
<flow:BaseView x:Class="MyApp.Views.OrderView"
               x:TypeArguments="vm:OrderViewModel"
               xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               xmlns:flow="clr-namespace:ElegantSeries.Flow.WPF.Hosting;assembly=ElegantSeries.Flow.WPF"
               xmlns:vm="clr-namespace:MyApp.ViewModels">
    <!-- ... -->
</flow:BaseView>
```

```csharp
public partial class OrderView : BaseView<OrderViewModel>
{
    public OrderView() => InitializeComponent();

    protected override void OnViewModelChanged(OrderViewModel? oldValue, OrderViewModel? newValue)
    {
        // View-side setup, e.g. subscribe to ViewModel events.
        // Called exactly once per change, on the UI thread.
    }
}
```

Views must keep a parameterless constructor (a XAML requirement); all
dependencies are injected into the ViewModel by DI.

## Threading model

The core navigation service may raise events on a thread-pool thread. The
`NavigationHost` marshals every UI update to the WPF dispatcher automatically.
View factories and `OnViewModelChanged` always run on the UI thread.

## Failure handling

If a view cannot be created (ViewModel type not registered, `ViewLocator` not
set, or a throwing view factory), the host keeps showing the previous content
and calls the protected `OnViewCreationFailed` hook. Override it to log the
error — rethrow there during development to fail fast on misconfiguration:

```csharp
public sealed class StrictHost : NavigationHost
{
    protected override void OnViewCreationFailed(INavigationViewModel vm, Exception ex)
        => throw new InvalidOperationException($"No view for {vm.GetType().Name}.", ex);
}
```

Note: the hook (rather than an exception) is the observable channel because the
core navigation service deliberately isolates event subscribers and swallows
their exceptions, so a misbehaving host can never corrupt navigation state.

## Versioning

| Package | Targets | Depends on |
|---|---|---|
| `ElegantSeries.Flow` | `net10.0` | — |
| `ElegantSeries.Flow.WPF` | `net10.0-windows` | `ElegantSeries.Flow` >= 0.1.0 |

## License

MIT — see [LICENSE](https://github.com/ligf/ElegantSeries.Flow/blob/main/LICENSE.txt).
