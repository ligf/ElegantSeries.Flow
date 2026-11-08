# ElegantSeries.Flow.Diagnostics

Diagnostics for [ElegantSeries.Flow](https://github.com/ligf/ElegantSeries.Flow):
a `System.Diagnostics.Metrics` observer that turns the navigation service's
public events into counters on the `ElegantSeries.Flow.Diagnostics` meter —
ready for `MeterListener` or any OpenTelemetry-compatible metrics pipeline.

The observer only subscribes to the four public events of
`INavigationService`; it does not wrap the service and adds no behavior to
navigation itself.

| Type | Purpose |
|---|---|
| `NavigationDiagnostics` | The observer: subscribes on construction, counts until disposed |
| `ServiceCollectionExtensions` | `services.AddElegantSeriesFlowDiagnostics()` DI registration |

Requires `ElegantSeries.Flow` **>= 1.0.0**.

## Counters

| Instrument | Counts |
|---|---|
| `navigation.completed` | Navigations that came to rest on a page (`RegionNavigated`). A navigation a guard vetoed never comes to rest and is not counted |
| `viewmodel.released` | Page ViewModels released by the navigation service (`ViewModelReleased`) |
| `cache.cleared` | Cache clears that removed at least one entry, per region (`RegionCacheCleared`); the event also fires once per affected region when the navigation service itself is disposed |
| `subscriber.errors` | Exceptions reported through `SubscriberError`: throwing event subscribers and navigation failures with no awaiting caller |

The counters count the public events exactly as they are raised — nothing
else is inferred or sampled.

## Usage

```csharp
services.AddElegantSeriesFlow();
services.AddElegantSeriesFlowDiagnostics();
```

Call `AddElegantSeriesFlow` first: the observer mirrors the lifetime of the
last unkeyed `INavigationService` registration (a singleton observer for a
singleton service, a scoped observer for a scoped one — a singleton observer
over a scoped service would observe the root scope's unused instance).
Calling it before the navigation service is registered, or over a transient
registration, throws `InvalidOperationException` instead of silently
observing nothing.

The observer subscribes when it is first resolved from the container and
counts from that moment; resolve `NavigationDiagnostics` once at startup if
counting must start with the first navigation. The container disposes it
with its provider or scope, which unsubscribes it.

Consume the meter with `MeterListener`, or with OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(NavigationDiagnostics.MeterName)); // "ElegantSeries.Flow.Diagnostics"
```

## AOT / trimming

The package is `IsAotCompatible=true`. Counting uses only the public event
surface of the core package — no reflection, no interception.

## Version correspondence

| This package | Requires |
|---|---|
| 1.0.0 | ElegantSeries.Flow >= 1.0.0 |

## License

MIT — see the [repository](https://github.com/ligf/ElegantSeries.Flow).
