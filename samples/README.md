# Samples

Two navigation samples (WPF + Avalonia, same feature set), included in the
main [ElegantSeries.Flow.slnx](../ElegantSeries.Flow.slnx). Open the solution
in your IDE and run the sample you need.

| Sample | Framework | How to run |
|--------|-----------|------------|
| [ElegantSeries.Flow.Samples.WPF](ElegantSeries.Flow.Samples.WPF) | WPF (`net10.0-windows`) | Open on Windows, run the project |
| [ElegantSeries.Flow.Samples.Avalonia](ElegantSeries.Flow.Samples.Avalonia) | Avalonia (`net10.0`) | `dotnet run` (any OS) |

Both samples share one ViewModel project
([ElegantSeries.Flow.Samples.Shared](ElegantSeries.Flow.Samples.Shared)): the
ViewModels are UI-framework-agnostic, so WPF and Avalonia reuse the exact same
classes — only the Views (XAML/AXAML) are platform-specific. The shared
`MenuViewModel` raises `OpenSecondWindowRequested` when the user asks for a
second window; each sample's `App` wires it to its own platform `SecondWindow`
(which demonstrates per-window scoped navigation with an isolated DI scope).

Both samples demonstrate the library's full feature set:

1. **DI setup** — `AddFlowNavigation()` (singleton); views + ViewModels
   registered AOT-safely via `AddFlowViews(...)`: `RegisterTransient` /
   `RegisterSingleton` combine view mapping with DI registration in one call
   (the menu ViewModel is a singleton — it coexists fine with transient
   pages: the page scope resolves the shared root instance and never
   disposes it), while the separate `services.AddTransient` + `views.Register`
   style is kept for Home/Detail to demonstrate the decoupled alternative
   for custom DI setup (factory, decorators, keyed services, ...).
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

   Note: navigating to the already-active page type is a no-op by design
   (per MAUI-style semantics), so push demos alternate between two page
   types rather than pushing the same type. Quadrant pages that can hit a
   no-op (e.g. Back at the root) show a one-line status hint instead of
   failing silently.
4. **External region control** — the "Push Q1" / "Back Q1" buttons above
   the quadrant grid drive Q1's region from outside the quadrant: regions
   are addressable by name, so any code holding the navigation service can
   navigate them.
5. **Typed parameters** — `NavigateToAsync<DetailViewModel, string>(...)`.
6. **KeepAlive** — the Counter page is navigated with
   `NavigationMode.KeepAlive`: increment, navigate away and back, the count is
   preserved because the page (ViewModel + scope) is cached, not disposed.
7. **Singleton ViewModel** — `MenuViewModel` is registered as singleton to show
   it coexists fine with transient pages: the page scope resolves the shared
   root instance and never disposes it.
8. **Transient recreation** — the Home page shows its instance id: navigate
   away and back, the id changes because the old page scope was disposed
   and a new one created.
9. **Guards** — the Guarded page implements both `INavigationGuard` and
   `INavigationGuardWithContext`; with "unsaved changes" on, navigating away
   is vetoed. The "Use plain guard (no context)" checkbox switches which
   logic runs — the framework always consults the context-aware guard first
   when implemented, so the checkbox demonstrates the observable difference
   (the plain guard cannot name the target page/region).
10. **Async lifecycle** — the Async Lifecycle page implements
    `INavigationAwareAsync<string>` with a visible log of `OnNavigatedToAsync` /
    `OnNavigatedFromAsync` and the strongly-typed navigation parameter.
11. **Async dispose** — the Async Dispose page implements `IAsyncDisposable`
    only: leave and come back, the instance id changed and the "async
    disposals" counter grew, proving the old page scope was disposed through
    the async path.
12. **ClearCache** — the Clear Cache page scripts the flow: open the
    KeepAlive Counter, increment, come back, `ClearCache("MainRegion")`,
    open the Counter again — the count is reset because the cached page was
    disposed. "Clear All Caches" calls `ClearAllCache()` for every region at
    once. The `...Async` buttons demonstrate the `ClearCacheAsync` /
    `ClearAllCacheAsync` variants.
13. **Cancellation** — the Cancellation page runs a navigation with an
    already-cancelled token, showing the `OperationCanceledException`
    contract.
14. **Custom view factory** — the Custom Factory page's view is built by a
    factory registered via `views.Register<TViewModel>(Func<TViewModel, TView>)`
    instead of `new TView()`; the factory stamps a visible badge on the view
    as proof. Use a factory when the view needs constructor arguments or
    other custom construction logic.
15. **View creation failure** — the View Failure page embeds a region driven
    to a ViewModel with no registered view: the host keeps the previous
    content and reports the failure through `OnViewCreationFailed`, surfaced
    here as a status line via a `NavigationHost` subclass instead of crashing
    the UI.
16. **Scoped multi-window** — "Second window" opens a window with its own DI
    scope and manually-constructed `NavigationService`; its stacks are fully
    isolated from the main window's. (This is the manual form of
    `services.AddScopedFlowNavigation()`: the extension is a root-container
    registration and cannot be combined with the app's `AddFlowNavigation()`
    singleton on the same container, since both use `TryAdd`.)
17. **Bound RegionName** — the Bound Region page data-binds a single
    `NavigationHost`'s `RegionName` (and `NavigationService`) instead of
    wiring them in code-behind. Flipping the bound region immediately
    re-displays the other region's current page; switching to an empty region
    clears the host.
18. **State inspector** — read-only navigation-state queries for MainRegion:
    `CanGoBack`, `IsActive<T>`, `GetCurrentMode`, `GetCurrentViewModel`, and
    `IViewLocator.IsRegistered<T>`.
19. **Navigation events** — `RegionNavigated`, `ViewModelReleased`, and
    `RegionCacheCleared` observed live: push/pop a temp page in region A and
    clear its KeepAlive cache while this page stays subscribed.
20. **Toolkit-free ViewModel** — the Toolkit-Free page's ViewModel derives from
    `NavigationViewModelBase` (core package): manual `SetProperty`
    notification, no CommunityToolkit.Mvvm; its buttons call public methods
    from code-behind.
21. **`ViewFor` attribute** — `HomeView` carries `[ViewFor(typeof(HomeViewModel))]`
    as the compile-time view→ViewModel declaration for the future source
    generator. The runtime still uses the manual `views.Register` call (it never
    scans the attribute); when the generator ships, the manual call goes away —
    never both.
