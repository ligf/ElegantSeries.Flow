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
3. **Quadrant regions** — the "Quadrants Demo" page hosts six quadrants
   (Q1–Q6), each an independent nested region running exactly one demo with
   its own buttons:
   - Q1 Stack — push deeper pages / go back inside the quadrant; the
     instance id proves a popped page is disposed and re-created on revisit;
   - Q2 KeepAlive — counter; the count survives leaving and coming back;
   - Q3 Typed Parameters — strongly-typed parameters pushed per button;
   - Q4 Replace — `NavigationMode.Replace` swaps the page without growing
     the stack;
   - Q5 ClearStack — `NavigationMode.ClearStack` resets to one fresh page;
   - Q6 Refresh — `refreshIfActive: true` re-runs activation on the same
     instance (id stays, activation count grows).
4. **Typed parameters** — `NavigateToAsync<DetailViewModel, string>(...)`.
5. **KeepAlive** — the Counter page is navigated with
   `NavigationMode.KeepAlive`: increment, navigate away and back, the count is
   preserved because the page (ViewModel + scope) is cached, not disposed.
6. **Singleton ViewModel** — `MenuViewModel` is registered as singleton to show
   it coexists fine with transient pages: the page scope resolves the shared
   root instance and never disposes it.
7. **Transient recreation** — the Home page shows its instance id: navigate
   away and back, the id changes because the old page scope was disposed
   and a new one created.
8. **Guards** — the Guarded page implements `INavigationGuardWithContext`;
   with "unsaved changes" on, navigating away is vetoed and the guard reports
   where you tried to go.
9. **Async lifecycle** — the Async Lifecycle page implements
   `INavigationAwareAsync` with a visible log of `OnNavigatedToAsync` /
   `OnNavigatedFromAsync`.
10. **ClearCache** — the Clear Cache page scripts the flow: open the
    KeepAlive Counter, increment, come back, `ClearCache("MainRegion")`,
    open the Counter again — the count is reset because the cached page was
    disposed.
11. **Cancellation** — the Cancellation page runs a navigation with an
    already-cancelled token, showing the `OperationCanceledException`
    contract.
12. **Scoped multi-window** — "Second window" opens a window with its own DI
    scope and manually-constructed `NavigationService`; its stacks are fully
    isolated from the main window's.
