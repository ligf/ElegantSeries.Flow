namespace ElegantSeries.Flow.Journal;

/// <summary>
/// The kind of a recorded <see cref="JournalEntry"/>.
/// </summary>
public enum JournalEntryKind
{
    /// <summary>A forward navigation (<c>NavigateToAsync</c> or
    /// <c>SetRootAsync</c>) completed successfully.</summary>
    Navigated,

    /// <summary>A backward navigation (<c>GoBackAsync</c> or
    /// <c>GoBackToAsync</c>) completed successfully.</summary>
    Back,

    /// <summary>At least one cached entry was actually removed from a
    /// region's KeepAlive cache (recorded from the navigation service's
    /// cache-cleared notification, so no-op clears record nothing).</summary>
    CacheCleared,

    /// <summary>A page's ViewModel was released (its page scope was
    /// disposed, or a singleton ViewModel was detached).</summary>
    Released,

    /// <summary>One or more stack entries were removed by a
    /// <c>Remove</c> call that returned a positive count.</summary>
    Removed,
}

/// <summary>
/// One recorded navigation-history entry. Entries are produced by
/// <see cref="JournalNavigationService"/> and persisted through an
/// <see cref="INavigationJournalStore"/>; the sequence number is assigned
/// by the journal in append order and is strictly increasing per journal
/// instance (see <see cref="NavigationJournal"/> remarks for the
/// cross-instance caveat).
/// </summary>
public sealed class JournalEntry
{
    /// <summary>The append-order sequence number, starting at 1.</summary>
    public long Sequence { get; init; }

    /// <summary>When the entry was recorded, in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>The region the entry belongs to (normalized name).</summary>
    public string Region { get; init; } = "";

    /// <summary>What happened.</summary>
    public JournalEntryKind Kind { get; init; }

    /// <summary>
    /// The assembly-qualified name of the ViewModel type involved: the
    /// navigated-to page for <see cref="JournalEntryKind.Navigated"/>, the
    /// page landed on for <see cref="JournalEntryKind.Back"/>, the released
    /// page for <see cref="JournalEntryKind.Released"/>, and the removal
    /// target for <see cref="JournalEntryKind.Removed"/>. Null for
    /// <see cref="JournalEntryKind.CacheCleared"/>.
    /// </summary>
    public string? ViewModelType { get; init; }

    /// <summary>
    /// The assembly-qualified name of the navigation parameter's type, or
    /// null when the navigation carried no parameter.
    /// </summary>
    public string? ParameterType { get; init; }

    /// <summary>
    /// The serialized parameter snapshot, frozen at call time with the
    /// serializer registered for the parameter type. Null when the
    /// navigation carried no parameter, when no serializer is registered
    /// for its type, or when serialization failed (the failure is reported
    /// through the navigation service's <c>SubscriberError</c> channel).
    /// </summary>
    public string? ParameterSnapshot { get; init; }
}
