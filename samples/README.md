# Samples

Two navigation samples (WPF + Avalonia, same feature set), included in the
main [ElegantSeries.Flow.slnx](../ElegantSeries.Flow.slnx). Open the solution
in your IDE and run the sample you need.

| Sample | Framework | How to run |
|--------|-----------|------------|
| [WpfSample](WpfSample) | WPF (`net10.0-windows`) | Open on Windows, run the project |
| [AvaloniaSample](AvaloniaSample) | Avalonia (`net10.0`) | `dotnet run` (any OS) |

Both samples demonstrate the library's full feature set:

1. **DI setup** — `AddFlowNavigation()` (singleton), ViewModels registered as
   transient, views registered AOT-safely via `AddFlowViews(...)`.
2. **Multi-region layout** — a `Sidebar` region (menu) and a `MainRegion`
   (content) navigate independently; both hosts share the singleton navigation
   service but keep separate stacks.
3. **Nested regions** — the Dashboard page hosts four quadrants (Q1–Q4), each
   an independent region. Q1 and Q4 show the same ViewModel *type* to prove
   each region keeps its own instance.
4. **Typed parameters** — `NavigateToAsync<DetailViewModel, string>(...)`.
5. **KeepAlive** — the Counter page is navigated with
   `NavigationMode.KeepAlive`: increment, navigate away and back, the count is
   preserved because the page (ViewModel + scope) is cached, not disposed.
6. **Singleton ViewModel** — `MenuViewModel` is registered as singleton to show
   it coexists fine with transient pages: the page scope resolves the shared
   root instance and never disposes it.
7. **Navigation modes** — the Features page demos `Replace` (swap current
   page), `ClearStack` (drop the stack back to Home), and the default `New`.
8. **Refresh** — `refreshIfActive: true` re-runs the active page's callbacks
   with a new parameter instead of pushing.
9. **Guards** — the Guarded page implements `INavigationGuardWithContext`;
   with "unsaved changes" on, navigating away is vetoed and the guard reports
   where you tried to go.
10. **Async lifecycle** — the Features page implements `INavigationAwareAsync`
    with a visible log of `OnNavigatedToAsync` / `OnNavigatedFromAsync`.
11. **ClearCache** — a button disposes cached KeepAlive pages on demand.
12. **Cancellation** — a demo navigation with an already-cancelled token shows
    the `OperationCanceledException` contract.
13. **Scoped multi-window** — "Second window" opens a window with its own DI
    scope and manually-constructed `NavigationService`; its stacks are fully
    isolated from the main window's.
