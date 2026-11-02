namespace ElegantSeries.Flow.Journal;

/// <summary>
/// The default <see cref="INavigationJournalStore"/>: entries and the
/// saved snapshot live in process memory only. Thread-safe.
/// </summary>
public sealed class InMemoryJournalStore : INavigationJournalStore
{
    private readonly object _lock = new();
    private readonly Queue<JournalEntry> _entries = new();
    private readonly int _maxEntries;
    private JournalSnapshot? _snapshot;

    /// <summary>Creates a store with the default options.</summary>
    public InMemoryJournalStore()
        : this(new JournalOptions())
    {
    }

    /// <summary>Creates a store with the given options.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="JournalOptions.MaxHistoryEntries"/> is not positive.</exception>
    public InMemoryJournalStore(JournalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxHistoryEntries <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxHistoryEntries,
                "MaxHistoryEntries must be positive.");
        _maxEntries = options.MaxHistoryEntries;
    }

    /// <inheritdoc/>
    public ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _maxEntries)
                _entries.Dequeue();
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<JournalEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            return new ValueTask<IReadOnlyList<JournalEntry>>(_entries.ToList());
        }
    }

    /// <inheritdoc/>
    public ValueTask SaveSnapshotAsync(JournalSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _snapshot = snapshot;
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            return new ValueTask<JournalSnapshot?>(_snapshot);
        }
    }

    /// <inheritdoc/>
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _entries.Clear();
            _snapshot = null;
        }
        return ValueTask.CompletedTask;
    }
}
