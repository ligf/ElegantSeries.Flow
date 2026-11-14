using System.Runtime.CompilerServices;

// The default-dispatcher selection is internal: the test project pins
// its priority directly because a running Application — the middle
// candidate the constructing thread's dispatcher must win over —
// cannot be created inside the test process.
[assembly: InternalsVisibleTo("ElegantSeries.Flow.WPF.Tests")]
