# Samples

Two navigation samples (WPF + Avalonia, same feature set), included in the
main [ElegantSeries.Flow.slnx](../ElegantSeries.Flow.slnx). Open the solution
in your IDE and run the sample you need.

| Sample | Framework | How to run |
|--------|-----------|------------|
| [ElegantSeries.Flow.Samples.WPF](ElegantSeries.Flow.Samples.WPF) | WPF (`net10.0-windows`) | Open on Windows, run the project |
| [ElegantSeries.Flow.Samples.Avalonia](ElegantSeries.Flow.Samples.Avalonia) | Avalonia (`net10.0`) | `dotnet run` (any OS) |

Both samples demonstrate the library's full feature set:

1. **DI setup** — `AddFlowNavigation()` (singleton), ViewModels registered as
   transient, views registered AOT-safely via `AddFlowViews(...)`.
2. **Multi-region layout** — the main window hosts five regions: a `Sidebar`
   (menu) plus four quadrants (Q1–Q4), each navigating independently. All
   hosts share the singleton navigation service but keep separate stacks.
   The menu drives Q1 only — the other quadrants keep their own content,
   proving regions navigate in isolation. Q1 and Q4 show the same ViewModel
   *type* to prove each region keeps its own instance.
3. **Typed parameters** — `NavigateToAsync<DetailViewModel, string>(...)`.
4. **KeepAlive** — the Counter page is navigated with
   `NavigationMode.KeepAlive`: increment, navigate away and back, the count is
   preserved because the page (ViewModel + scope) is cached, not disposed.
5. **Singleton ViewModel** — `MenuViewModel` is registered as singleton to show
   it coexists fine with transient pages: the page scope resolves the shared
   root instance and never disposes it.
6. **Navigation modes** — the Features page demos `Replace` (swap current
   page), `ClearStack` (drop the stack back to Home), and the default `New`.
7. **Refresh** — `refreshIfActive: true` re-runs the active page's callbacks
   with a new parameter instead of pushing.
8. **Guards** — the Guarded page implements `INavigationGuardWithContext`;
   with "unsaved changes" on, navigating away is vetoed and the guard reports
   where you tried to go.
9. **Async lifecycle** — the Features page implements `INavigationAwareAsync`
    with a visible log of `OnNavigatedToAsync` / `OnNavigatedFromAsync`.
10. **ClearCache** — a button disposes cached KeepAlive pages on demand.
11. **Cancellation** — a demo navigation with an already-cancelled token shows
    the `OperationCanceledException` contract.
12. **Scoped multi-window** — "Second window" opens a window with its own DI
    scope and manually-constructed `NavigationService`; its stacks are fully
    isolated from the main window's.
