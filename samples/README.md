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
2. **Multi-region layout** — the main window hosts a `Sidebar` region (menu)
   and a `MainRegion` (full-page content); both share the singleton
   navigation service but keep separate stacks. Menu buttons switch the
   whole right side, like a web sidebar.
3. **Quadrant regions** — the "四宫格演示" page hosts four quadrants
   (Q1–Q4), each an independent nested region with its own demo and its own
   buttons:
   - Q1 页面栈 — push deeper pages / go back inside the quadrant;
   - Q2 状态保持 — `KeepAlive` counter, the count survives leaving and
     coming back;
   - Q3 参数传递 — strongly-typed parameters pushed per button;
   - Q4 导航模式 — `New` / `Replace` / `ClearStack` / `refreshIfActive`,
     with an instance id proving reuse vs. recreation.
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
