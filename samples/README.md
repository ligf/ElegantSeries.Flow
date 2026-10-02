# Samples

Minimal two-page navigation samples. They are standalone projects (not part of
`ElegantSeries.Flow.slnx` and not built by CI) — open the folder you need in your
IDE and run.

| Sample | Framework | How to run |
|--------|-----------|------------|
| [WpfSample](WpfSample) | WPF (`net10.0-windows`) | Open on Windows, run the project |
| [AvaloniaSample](AvaloniaSample) | Avalonia (`net10.0`) | `dotnet run` (any OS) |

Both samples do the same thing:

1. Register core navigation + two ViewModels (`HomeViewModel`, `DetailViewModel`) with DI.
2. Register the views AOT-safely via `services.AddFlowViews(...)`.
3. Show a `NavigationHost` (`RegionName="MainRegion"`) in the main window.
4. Navigate Home → Detail with a typed string parameter, and back.
