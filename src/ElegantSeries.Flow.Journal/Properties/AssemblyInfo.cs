using System.Runtime.CompilerServices;

// Journal tests stage disposal/enqueue interleavings through the
// decorator's internal test seam (EnqueueBoundaryHook); nothing else
// consumes Journal internals.
[assembly: InternalsVisibleTo("ElegantSeries.Flow.Journal.Tests")]
