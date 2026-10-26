using System.Runtime.CompilerServices;

// The navigation host's attach/detach bookkeeping is internal: the test
// project drives it directly because a visual tree (and its event-args
// type) cannot be constructed in a unit test.
[assembly: InternalsVisibleTo("ElegantSeries.Flow.Avalonia.Tests")]
