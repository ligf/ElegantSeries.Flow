namespace ElegantSeries.Flow.Journal;

/// <summary>
/// The default <see cref="INavigationJournalStore"/>: entries and the
/// saved snapshot live in process memory only. Thread-safe. Snapshots
/// are copied on save and on load, so a caller mutating a snapshot it
/// handed in — or one it got back — never rewrites the saved one (the
/// isolation the JSON file store gets from serialization).
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
            _snapshot = CloneSnapshot(snapshot);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            return new ValueTask<JournalSnapshot?>(
                _snapshot is null ? null : CloneSnapshot(_snapshot));
        }
    }

    private static JournalSnapshot CloneSnapshot(JournalSnapshot snapshot) => new()
    {
        FormatVersion = snapshot.FormatVersion,
        CapturedUtc = snapshot.CapturedUtc,
        Regions = snapshot.Regions
            .Select(region => new JournalRegionSnapshot
            {
                Region = region.Region,
                Pages = region.Pages
                    .Select(page => new JournalPageSnapshot
                    {
                        ViewModelType = page.ViewModelType,
                        ParameterType = page.ParameterType,
                        Parameter = page.Parameter,
                        Retention = page.Retention,
                    })
                    .ToList(),
            })
            .ToList(),
    };

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
