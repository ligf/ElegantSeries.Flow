# ElegantSeries.Flow.Journal

Navigation journal for [ElegantSeries.Flow](https://github.com/ligf/ElegantSeries.Flow):
a decorator over `INavigationService` that records navigation history with
parameter snapshots, persists it through a pluggable store, and restores
region stacks from a captured snapshot — session restore and navigation
auditing without touching the core service.

The core navigation service knows nothing about the journal; the package
only uses its public surface.

| Type | Purpose |
|---|---|
| `JournalNavigationService` | The recording decorator, registered in place of the navigation service |
| `INavigationJournal` / `NavigationJournal` | Reads entries, captures and loads snapshots, restores stacks |
| `JournalEntry` / `JournalSnapshot` | The recorded entries (`Navigated`, `Back`, `Removed`, `CacheCleared`, `Released`) and the stack snapshot (format version 1) |
| `InMemoryJournalStore` / `JsonFileNavigationJournalStore` | The built-in stores; `INavigationJournalStore` is the custom-store seam |
| `JournalBuilder` | Configuration: parameter serializers, store choice, history cap |

Requires `ElegantSeries.Flow` **>= 1.0.0**.

## Usage

```csharp
services.AddElegantSeriesFlow();
services.AddElegantSeriesFlowJournal(journal =>
{
    // Parameter snapshots are opaque strings produced by your delegates —
    // no reflection, no built-in serializer.
    journal.RegisterParameter<OrderParameter>(
        p => p.Id.ToString(CultureInfo.InvariantCulture),
        s => new OrderParameter(int.Parse(s, CultureInfo.InvariantCulture)));

    journal.UseJsonFileStore("journal.json"); // optional; the default store is in-memory
});
```

Call `AddElegantSeriesFlow` first: the journal wraps the last unkeyed
`INavigationService` registration and keeps its lifetime. Afterwards,
`INavigationService` resolves to the decorator, so navigations through it
are recorded. Registering the journal twice is a no-op — the service is
never wrapped twice.

## Entries and snapshots

```csharp
var entries = await journal.GetEntriesAsync();
var snapshot = await journal.CaptureSnapshotAsync(); // the live stacks, per region
```

- A parameter's snapshot is frozen **at call time** for calls through the
  decorator. Parameter types without a registered serializer pair are
  journaled with a null snapshot and restore with a null parameter.
- Navigations that bypass the decorator (a page navigating through its own
  `Navigation` property) are recorded from the settle announcement, with
  the parameter frozen at settle time.
- History is a ring capped by `JournalOptions.MaxHistoryEntries`
  (default 1000); both built-in stores enforce the cap.
- A snapshot captures the live stacks only; the KeepAlive cache is not part
  of a snapshot and is not restored.

## Restore

Restore replays a snapshot one page at a time through a delegate you
supply — the journal never resolves view-model types itself. `Operation`
is `Root` for a region's first page and `Push` for the rest:

```csharp
var result = await journal.RestoreAsync(snapshot, page =>
{
    // ViewModelType is the assembly-qualified type name; pass the recorded
    // retention through so restored pages keep their original lifetime.
    if (page.ViewModelType == typeof(HomeViewModel).AssemblyQualifiedName)
    {
        return page.Operation == JournalRestoreOperation.Root
            ? navigation.SetRootAsync<HomeViewModel>(page.Region, page.Retention)
            : navigation.NavigateToAsync<HomeViewModel>(
                page.Region, NavigationOperation.Push, page.Retention);
    }
    // ... one check per recorded page type
    return Task.FromResult(false);
});
// result.AllSucceeded / per-region JournalRegionRestoreResult
```

A region whose restore fails stops at the failed page; the remaining
regions still restore, and the outcome is reported in the returned
`JournalRestoreResult` instead of being thrown.

One fidelity limit, also stated in the package's API documentation:
navigations registered from inside a lifecycle callback are recorded at
registration time. If such a registration is later superseded by another
registration in the same window, or vetoed by a leave guard, the page never
lands but its entry remains — and a restore will navigate to it.

## AOT / trimming

The package is `IsAotCompatible=true`. Parameter (de)serialization is
entirely delegate-based; the package performs no reflection.

## Version correspondence

| This package | Requires |
|---|---|
| 1.0.0 | ElegantSeries.Flow >= 1.0.0 |

## License

MIT — see the [repository](https://github.com/ligf/ElegantSeries.Flow).
