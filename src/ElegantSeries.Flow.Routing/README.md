# ElegantSeries.Flow.Routing

Routing for [ElegantSeries.Flow](https://github.com/ligf/ElegantSeries.Flow):
an explicit, reflection-free route table that maps URIs to typed
navigations — deep links and URI-driven navigation without stringly-typed
page keys at the call site.

Routes are registered as patterns on a builder and compiled into closures
over `INavigationService.NavigateToAsync`; matching a URI is a segment
walk — no reflection and no route-expression parsing at run time.

| Type | Purpose |
|---|---|
| `RouteTableBuilder` / `RouteTable` | Route registrations, and the immutable table built from them |
| `IRouter` / `Router` | Matches a URI against the table and performs the registered navigation |
| `RouteMatch` | A matched URI: its segments, the placeholder `Values`, and the `Query` |
| `RouteNotFoundException` | Thrown by `NavigateAsync` when no route matches (sealed) |
| `ServiceCollectionExtensions` | `services.AddElegantSeriesFlowRouting(routes => ...)` DI registration |

Requires `ElegantSeries.Flow` **>= 1.0.0**.

## Usage

```csharp
services.AddElegantSeriesFlow();
services.AddElegantSeriesFlowRouting(routes =>
{
    routes.Map<HomeViewModel>("home");
    routes.Map<OrdersViewModel>("orders");
    routes.Map<OrderDetailsViewModel, OrderParameter>(
        "orders/{id}",
        match => new OrderParameter(int.Parse(match.Values["id"], CultureInfo.InvariantCulture)));
});

// Anywhere (e.g. a deep-link handler); IRouter is resolved from DI:
await router.NavigateAsync("orders/42?expand=items");  // throws RouteNotFoundException if unmatched
bool ok = await router.TryNavigateAsync("orders/42");  // returns false instead
```

Call `AddElegantSeriesFlow` first: the router mirrors the lifetime of the
last unkeyed `INavigationService` registration. Registering it before the
navigation service, or over a transient registration, throws
`InvalidOperationException`.

Each `Map` call can also pick the region, the navigation operation, and the
retention (defaults: `MainRegion`, `Push`, `Destroy`).

## Matching rules

- Patterns and URIs are split into `/`-separated segments; a route matches
  only a URI with the same number of segments. Entries are tried in
  registration order; the first full match wins.
- A `{name}` segment is a placeholder and captures that segment into
  `RouteMatch.Values`; literal segments compare case-insensitively
  (`OrdinalIgnoreCase`).
- The query string is exposed as `RouteMatch.Query`. A pair beginning with
  `=` produces an empty key, as the browser's `URLSearchParams` does.
- A full URI (`"app://orders/42"`) is accepted: the `scheme://` prefix is
  stripped and the remainder treated as the path (a desktop deep link's
  authority carries no routing meaning); any fragment is ignored. `""` and
  `"/"` address the root route.
- Both `NavigateAsync` and `TryNavigateAsync` return the navigation's fate
  exactly as the navigation service reports it: `true` when the navigation
  was executed, `false` when it was vetoed, superseded, or discarded.

## AOT / trimming

The package is `IsAotCompatible=true`. Parameter factories are your own
delegates over `RouteMatch`; the package itself performs no reflection.

## Version correspondence

| This package | Requires |
|---|---|
| 1.0.0 | ElegantSeries.Flow >= 1.0.0 |

## License

MIT — see the [repository](https://github.com/ligf/ElegantSeries.Flow).
