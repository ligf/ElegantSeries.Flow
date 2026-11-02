using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElegantSeries.Flow.Journal;

/// <summary>
/// An <see cref="INavigationJournalStore"/> that persists entries and the
/// saved snapshot as plain JSON files, so a journal survives process
/// restarts. Entries live in the file at the given path; the snapshot
/// lives beside it with <c>.snapshot</c> inserted before the extension.
/// </summary>
/// <remarks>
/// <para>
/// The files are plain, unencrypted JSON. Parameter snapshots can contain
/// sensitive data: choosing the path and protecting the files is the
/// application's responsibility.
/// </para>
/// <para>
/// Every append rewrites the entries file (through a temporary file that
/// is moved over the target), which is sized for the capped history
/// (<see cref="JournalOptions.MaxHistoryEntries"/>). A file whose content
/// is not valid journal JSON surfaces as an exception on the next
/// operation instead of being silently discarded.
/// </para>
/// </remarks>
public sealed class JsonFileNavigationJournalStore : INavigationJournalStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _entriesPath;
    private readonly string _snapshotPath;
    private readonly int _maxEntries;
    private List<JournalEntry>? _entries;
    private bool _disposed;

    /// <summary>Creates a store writing to the given entries file path,
    /// with the default options.</summary>
    public JsonFileNavigationJournalStore(string path)
        : this(path, new JournalOptions())
    {
    }

    /// <summary>Creates a store writing to the given entries file path.</summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null
    /// or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="JournalOptions.MaxHistoryEntries"/> is not positive.</exception>
    public JsonFileNavigationJournalStore(string path, JournalOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxHistoryEntries <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxHistoryEntries,
                "MaxHistoryEntries must be positive.");
        _entriesPath = path;
        _maxEntries = options.MaxHistoryEntries;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var snapshotName = Path.GetFileNameWithoutExtension(path) + ".snapshot" + Path.GetExtension(path);
        _snapshotPath = Path.Combine(directory, snapshotName);
    }

    /// <inheritdoc/>
    public async ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Build the successor list on the side and publish it as
            // the cache only after the write succeeds: a failed write
            // must not leave the entry in the cached list, where the
            // next successful append would silently persist it.
            var loaded = await LoadEntriesCoreAsync(cancellationToken).ConfigureAwait(false);
            var updated = new List<JournalEntry>(loaded) { entry };
            while (updated.Count > _maxEntries)
                updated.RemoveAt(0);
            await WriteEntriesCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            _entries = updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<JournalEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadEntriesCoreAsync(cancellationToken).ConfigureAwait(false);
            return entries.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask SaveSnapshotAsync(JournalSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(snapshot, JournalJsonContext.Default.JournalSnapshot);
            await WriteFileCoreAsync(_snapshotPath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_snapshotPath))
                return null;
            var json = await File.ReadAllTextAsync(_snapshotPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, JournalJsonContext.Default.JournalSnapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Delete first, clear the cache last: a failed delete must
            // not leave this instance believing the history is gone
            // while the file (and any other instance reading it) still
            // holds it.
            File.Delete(_entriesPath);
            File.Delete(_snapshotPath);
            _entries = [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the store's synchronization primitive. The store
    /// must not be used afterwards.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
    }

    private async ValueTask<List<JournalEntry>> LoadEntriesCoreAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null)
            return _entries;
        if (!File.Exists(_entriesPath))
        {
            _entries = [];
            return _entries;
        }
        var json = await File.ReadAllTextAsync(_entriesPath, cancellationToken).ConfigureAwait(false);
        _entries = JsonSerializer.Deserialize(json, JournalJsonContext.Default.ListJournalEntry) ?? [];
        return _entries;
    }

    private async ValueTask WriteEntriesCoreAsync(List<JournalEntry> entries, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(entries, JournalJsonContext.Default.ListJournalEntry);
        await WriteFileCoreAsync(_entriesPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteFileCoreAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<JournalEntry>))]
[JsonSerializable(typeof(JournalSnapshot))]
internal sealed partial class JournalJsonContext : JsonSerializerContext
{
}
