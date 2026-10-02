# Samples

Two navigation samples (WPF + Avalonia, same feature set). They are standalone
projects (not part of the main `ElegantSeries.Flow.slnx` and not built by CI) —
open [ElegantSeries.Flow.Samples.slnx](ElegantSeries.Flow.Samples.slnx) in your
IDE to work with both, or open the folder you need and run.

| Sample | Framework | How to run |
|--------|-----------|------------|
| [WpfSample](WpfSample) | WPF (`net10.0-windows`) | Open on Windows, run the project |
| [AvaloniaSample](AvaloniaSample) | Avalonia (`net10.0`) | `dotnet run` (any OS) |

Both samples demonstrate:

1. **DI setup** — `AddFlowNavigation()` (singleton), ViewModels registered as
   transient, views registered AOT-safely via `AddFlowViews(...)`.
2. **Multi-region layout** — a `Sidebar` region (menu) and a `MainRegion`
   (content) navigate independently; both hosts share the singleton navigation
   service but keep separate stacks.
3. **Typed parameters** — `NavigateToAsync<DetailViewModel, string>(...)`.
4. **KeepAlive** — the Counter page is navigated with
   `NavigationMode.KeepAlive`: increment, navigate away and back, the count is
   preserved because the page (ViewModel + scope) is cached, not disposed.
5. **Singleton ViewModel** — `MenuViewModel` is registered as singleton to show
   it coexists fine with transient pages: the page scope resolves the shared
   root instance and never disposes it.
