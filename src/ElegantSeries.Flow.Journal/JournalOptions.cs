namespace ElegantSeries.Flow.Journal;

/// <summary>
/// Options for the navigation journal, configured through
/// <see cref="JournalBuilder"/>.
/// </summary>
public sealed class JournalOptions
{
    /// <summary>
    /// The maximum number of history entries the built-in stores retain.
    /// Appending beyond the cap drops the oldest entries (the saved stack
    /// snapshot is stored separately and is not affected). Defaults to
    /// 1000 and must be positive.
    /// </summary>
    public int MaxHistoryEntries { get; set; } = 1000;
}
