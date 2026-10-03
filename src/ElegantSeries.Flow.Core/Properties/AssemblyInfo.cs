using System.Runtime.CompilerServices;

// Shared-host internals (NavigationHostController<TView>, ViewRegistry<TView>) are
// UI-framework-agnostic but stay internal: the platform packages
// (ElegantSeries.Flow.WPF, ElegantSeries.Flow.Avalonia) are the only consumers.
// Public API surface is unchanged by the move.

[assembly: InternalsVisibleTo("ElegantSeries.Flow.WPF")]
[assembly: InternalsVisibleTo("ElegantSeries.Flow.Avalonia")]

// The Mvvm package's BaseViewModel explicitly implements
// INavigationViewModel, whose Navigation setter is internal by design
// (only the navigation service may attach/detach it). The friend assembly
// grant lets the first-party base class provide that implementation without
// widening the setter to public for all consumers.
[assembly: InternalsVisibleTo("ElegantSeries.Flow.Mvvm")]

// Logic tests compile against Core only (no UI runtime) and exercise the
// shared host internals directly.
[assembly: InternalsVisibleTo("ElegantSeries.Flow.WPF.Tests")]
