namespace ElegantSeries.Flow.Journal;

/// <summary>
/// Persistence for journal entries and the latest stack snapshot.
/// Implementations must tolerate concurrent calls: the journal serializes
/// its own appends, but reads, snapshot saves/loads, and clears can race
/// them.
/// </summary>
/// <remarks>
/// Built-in implementations: <see cref="InMemoryJournalStore"/> (default)
/// and <see cref="JsonFileNavigationJournalStore"/>. Both enforce the
/// history cap (<see cref="JournalOptions.MaxHistoryEntries"/>): appending
/// beyond the cap drops the oldest entries. The saved snapshot is stored
/// separately and is not subject to the cap.
/// </remarks>
/// <remarks>
/// Implementations must not call back into the navigation service or the
/// journal facade: appends run inside the journal's recording pipeline,
/// and a callback into a navigation (or a snapshot capture) would
/// deadlock against it.
/// </remarks>
public interface INavigationJournalStore
{
    /// <summary>Appends one entry, dropping the oldest entries when the
    /// history cap is exceeded.</summary>
    ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Returns the retained entries, oldest first.</summary>
    ValueTask<IReadOnlyList<JournalEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the saved snapshot.</summary>
    ValueTask SaveSnapshotAsync(JournalSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Returns the saved snapshot, or null when none was
    /// saved.</summary>
    ValueTask<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes all entries and the saved snapshot.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
