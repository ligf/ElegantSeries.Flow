using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Journal;

/// <summary>
/// A point-in-time picture of every region's navigation stack, captured
/// by <see cref="INavigationJournal.CaptureSnapshotAsync"/> and consumed
/// by <see cref="INavigationJournal.RestoreAsync"/>.
/// </summary>
/// <remarks>
/// The format carries <see cref="FormatVersion"/> from its first version
/// and evolves additively only. The KeepAlive cache is deliberately not
/// part of a snapshot and is never restored: only stack pages are.
/// </remarks>
public sealed class JournalSnapshot
{
    /// <summary>The snapshot format version. This build writes and reads
    /// version 1; restoring a snapshot with a higher version throws
    /// <see cref="NotSupportedException"/>.</summary>
    public int FormatVersion { get; init; } = 1;

    /// <summary>When the snapshot was captured, in UTC.</summary>
    public DateTimeOffset CapturedUtc { get; init; }

    /// <summary>One entry per region that held a non-empty stack at
    /// capture time.</summary>
    public IReadOnlyList<JournalRegionSnapshot> Regions { get; init; } = [];
}

/// <summary>One region's stack within a <see cref="JournalSnapshot"/>.</summary>
public sealed class JournalRegionSnapshot
{
    /// <summary>The region name (normalized).</summary>
    public string Region { get; init; } = "";

    /// <summary>The region's pages, bottom of stack first.</summary>
    public IReadOnlyList<JournalPageSnapshot> Pages { get; init; } = [];
}

/// <summary>One stack page within a <see cref="JournalRegionSnapshot"/>.</summary>
public sealed class JournalPageSnapshot
{
    /// <summary>The assembly-qualified name of the page's ViewModel
    /// type.</summary>
    public string ViewModelType { get; init; } = "";

    /// <summary>The assembly-qualified name of the page's navigation
    /// parameter type, or null when the page was navigated to without a
    /// parameter.</summary>
    public string? ParameterType { get; init; }

    /// <summary>The serialized parameter snapshot, or null when no
    /// serializer was registered for the parameter type (or serialization
    /// failed) at navigation time. A null parameter is restored as a null
    /// parameter.</summary>
    public string? Parameter { get; init; }

    /// <summary>The retention the page was navigated with.</summary>
    public NavigationRetention Retention { get; init; }
}

/// <summary>How a page is restored by the restore delegate.</summary>
public enum JournalRestoreOperation
{
    /// <summary>The first page of a region: restore it as the region's
    /// root (reset the region onto this page).</summary>
    Root,

    /// <summary>Every subsequent page: push it onto the region's
    /// stack.</summary>
    Push,
}

/// <summary>
/// One page handed to the restore delegate of
/// <see cref="INavigationJournal.RestoreAsync"/>.
/// </summary>
public sealed class JournalPageRestore
{
    /// <summary>The region the page belongs to.</summary>
    public string Region { get; init; } = "";

    /// <summary>The assembly-qualified name of the page's ViewModel
    /// type.</summary>
    public string ViewModelType { get; init; } = "";

    /// <summary><see cref="JournalRestoreOperation.Root"/> for the first
    /// page of a region, <see cref="JournalRestoreOperation.Push"/> for
    /// the rest.</summary>
    public JournalRestoreOperation Operation { get; init; }

    /// <summary>
    /// The page's parameter, materialized with the deserializer
    /// registered for its type. Null when the snapshot carries no
    /// parameter for the page. When the snapshot carries a serialized
    /// parameter but no deserializer is registered, the page is not
    /// handed to the delegate at all: it counts as that region's failed
    /// page instead.
    /// </summary>
    public object? Parameter { get; init; }

    /// <summary>The retention the page was originally navigated
    /// with.</summary>
    public NavigationRetention Retention { get; init; }
}

/// <summary>
/// The outcome of <see cref="INavigationJournal.RestoreAsync"/>: one
/// result per snapshot region.
/// </summary>
public sealed class JournalRestoreResult
{
    /// <summary>Per-region outcomes, in snapshot order.</summary>
    public IReadOnlyList<JournalRegionRestoreResult> Regions { get; init; } = [];

    /// <summary>True when every page of every region was restored.</summary>
    public bool AllSucceeded => Regions.All(r => r.Succeeded);
}

/// <summary>The outcome of restoring one region of a snapshot.</summary>
public sealed class JournalRegionRestoreResult
{
    /// <summary>The region name.</summary>
    public string Region { get; init; } = "";

    /// <summary>How many of the region's pages were restored before the
    /// region completed or stopped.</summary>
    public int RestoredPages { get; init; }

    /// <summary>How many pages the region's snapshot holds.</summary>
    public int TotalPages { get; init; }

    /// <summary>The page at which restoration of this region stopped, or
    /// null when the region was fully restored.</summary>
    public JournalPageSnapshot? FailedPage { get; init; }

    /// <summary>The exception the restore delegate threw for
    /// <see cref="FailedPage"/> — or the exception the registered
    /// parameter deserializer threw for it — or null when the page failed
    /// by returning false or by having no registered deserializer.</summary>
    public Exception? Error { get; init; }

    /// <summary>True when every page of the region was restored.</summary>
    public bool Succeeded => FailedPage is null;
}
