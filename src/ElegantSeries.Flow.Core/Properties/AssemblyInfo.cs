using System.Runtime.CompilerServices;

// Shared-host internals (NavigationHostController<TView>, ViewRegistry<TView>) are
// UI-framework-agnostic but stay internal: the platform packages
// (ElegantSeries.Flow.WPF, ElegantSeries.Flow.Avalonia) are the only consumers.
// Public API surface is unchanged by the move.

[assembly: InternalsVisibleTo("ElegantSeries.Flow.WPF")]
[assembly: InternalsVisibleTo("ElegantSeries.Flow.Avalonia")]

// Logic tests compile against Core only (no UI runtime) and exercise the
// shared host internals directly.
[assembly: InternalsVisibleTo("ElegantSeries.Flow.WPF.Tests")]
