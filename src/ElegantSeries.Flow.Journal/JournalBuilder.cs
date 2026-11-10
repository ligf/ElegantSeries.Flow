namespace ElegantSeries.Flow.Journal;

/// <summary>
/// Configures the navigation journal inside
/// <c>AddElegantSeriesFlowJournal</c>: parameter serializers, the journal
/// store, and <see cref="JournalOptions"/>.
/// </summary>
/// <remarks>
/// A builder is a registration-time object: it is consumed while
/// <c>AddElegantSeriesFlowJournal</c> registers. Mutating a builder
/// after registration — including calling <c>RegisterParameter</c> on a
/// reference retained from the configure delegate — is not supported:
/// the journal reads the registered serializers on navigating threads,
/// and a post-registration change races those reads.
/// </remarks>
public sealed class JournalBuilder
{
    internal JournalParameterSerializers Serializers { get; } = new();
    internal INavigationJournalStore? StoreInstance { get; private set; }
    internal string? JsonFilePath { get; private set; }

    /// <summary>The journal options (history cap).</summary>
    public JournalOptions Options { get; } = new();

    /// <summary>
    /// Registers the serializer pair for one navigation parameter type.
    /// Only parameters of registered types get a serialized snapshot in
    /// the journal and in stack snapshots; unregistered types are
    /// journaled with a null snapshot and restore as a null parameter.
    /// The delegates run on the navigating thread at call time (serialize)
    /// and on the restoring thread (deserialize), must be thread-safe if
    /// the same registration serves concurrent windows, and may use any
    /// format — the journal treats snapshots as opaque strings. Registering
    /// the same type twice replaces the earlier pair.
    /// </summary>
    public JournalBuilder RegisterParameter<TParameter>(
        Func<TParameter, string> serialize,
        Func<string, TParameter> deserialize)
    {
        ArgumentNullException.ThrowIfNull(serialize);
        ArgumentNullException.ThrowIfNull(deserialize);
        Serializers.Add(serialize, deserialize);
        return this;
    }

    /// <summary>
    /// Uses the given store instance instead of a built-in one. The
    /// instance is registered as a singleton; like any instance
    /// registration it remains owned by the caller — the container does
    /// not dispose it. An explicitly configured store wins over a store
    /// the application registered directly in the service collection.
    /// </summary>
    public JournalBuilder UseStore(INavigationJournalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        StoreInstance = store;
        JsonFilePath = null;
        return this;
    }

    /// <summary>
    /// Uses a <see cref="JsonFileNavigationJournalStore"/> writing to the
    /// given entries file path, so the journal survives process restarts.
    /// An explicitly configured store wins over a store the application
    /// registered directly in the service collection.
    /// </summary>
    public JournalBuilder UseJsonFileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        JsonFilePath = path;
        StoreInstance = null;
        return this;
    }
}
