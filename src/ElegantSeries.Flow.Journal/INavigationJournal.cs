namespace ElegantSeries.Flow.Journal;

/// <summary>
/// The read/restore side of the navigation journal, resolved from the
/// container after <c>AddElegantSeriesFlowJournal</c>. Recording happens
/// automatically through the decorated <c>INavigationService</c>; this
/// facade reads entries, captures stack snapshots, and restores them.
/// </summary>
/// <remarks>
/// Restore is not transactional: while a restore runs, other navigations
/// on the same service interleave with the restored pages. Restore into a
/// quiet service (typically at startup, before the first window
/// navigates).
/// </remarks>
public interface INavigationJournal
{
    /// <summary>Returns the retained history entries, oldest first.</summary>
    Task<IReadOnlyList<JournalEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the current stacks of all regions in a single atomic
    /// read and saves the snapshot to the store (replacing the
    /// previously saved one). Pages carry the parameter snapshot and
    /// retention they were navigated with; the KeepAlive cache is not
    /// captured.
    /// </summary>
    Task<JournalSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the snapshot most recently saved through
    /// <see cref="CaptureSnapshotAsync"/> (or by a previous process, for
    /// persistent stores), or null when none exists.</summary>
    Task<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a snapshot region by region: for each region the delegate
    /// is called once per page — <see cref="JournalRestoreOperation.Root"/>
    /// for the first page, <see cref="JournalRestoreOperation.Push"/> for
    /// the rest — and performs that page's navigation, returning the
    /// navigation's fate. When a page fails (the delegate returns false,
    /// throws, or the page's serialized parameter has no registered
    /// deserializer), restoration of that region stops at the failed page
    /// — pages already restored stay — and the remaining regions are
    /// still restored. Per-region outcomes are reported in the returned
    /// <see cref="JournalRestoreResult"/>; a page failure never makes this
    /// method throw.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/>
    /// or <paramref name="navigatePage"/> is null.</exception>
    /// <exception cref="NotSupportedException">The snapshot's
    /// <see cref="JournalSnapshot.FormatVersion"/> is higher than this
    /// build supports.</exception>
    Task<JournalRestoreResult> RestoreAsync(
        JournalSnapshot snapshot,
        Func<JournalPageRestore, Task<bool>> navigatePage,
        CancellationToken cancellationToken = default);

    /// <summary>Removes all history entries and the saved snapshot from
    /// the store.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="INavigationJournal"/> implementation, bound to one
/// decorated navigation service (same lifetime).
/// </summary>
public sealed class NavigationJournal : INavigationJournal
{
    private const int SupportedFormatVersion = 1;

    private readonly JournalNavigationService _navigation;
    private readonly INavigationJournalStore _store;

    /// <summary>Creates the facade over one decorated navigation service
    /// and its store. Normally resolved from the container after
    /// <c>AddElegantSeriesFlowJournal</c>.</summary>
    public NavigationJournal(JournalNavigationService navigation, INavigationJournalStore store)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(store);
        _navigation = navigation;
        _store = store;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JournalEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        await _store.ReadEntriesAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<JournalSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _navigation.CaptureSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
        await _store.SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    /// <inheritdoc/>
    public async Task<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
        await _store.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<JournalRestoreResult> RestoreAsync(
        JournalSnapshot snapshot,
        Func<JournalPageRestore, Task<bool>> navigatePage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(navigatePage);
        if (snapshot.FormatVersion > SupportedFormatVersion)
            throw new NotSupportedException(
                $"Journal snapshot format version {snapshot.FormatVersion} is not supported by this build " +
                $"(maximum supported: {SupportedFormatVersion}).");

        var serializers = _navigation.Serializers;
        var regionResults = new List<JournalRegionRestoreResult>();
        foreach (var region in snapshot.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            regionResults.Add(await RestoreRegionAsync(region, navigatePage, serializers, cancellationToken).ConfigureAwait(false));
        }
        return new JournalRestoreResult { Regions = regionResults };
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default) =>
        await _store.ClearAsync(cancellationToken).ConfigureAwait(false);

    private static async Task<JournalRegionRestoreResult> RestoreRegionAsync(
        JournalRegionSnapshot region,
        Func<JournalPageRestore, Task<bool>> navigatePage,
        JournalParameterSerializers serializers,
        CancellationToken cancellationToken)
    {
        var restored = 0;
        foreach (var (page, index) in region.Pages.Select((page, index) => (page, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool fate;
            try
            {
                object? parameter = null;
                if (page.Parameter is not null)
                {
                    // A serialized parameter with no registered deserializer
                    // (or an unnamed type) is a page failure, not a throw.
                    if (page.ParameterType is null ||
                        !serializers.TryDeserialize(page.ParameterType, page.Parameter, out parameter))
                    {
                        return Failure(region, restored, page, error: null);
                    }
                }

                fate = await navigatePage(new JournalPageRestore
                {
                    Region = region.Region,
                    ViewModelType = page.ViewModelType,
                    Operation = index == 0 ? JournalRestoreOperation.Root : JournalRestoreOperation.Push,
                    Parameter = parameter,
                    Retention = page.Retention,
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failure(region, restored, page, ex);
            }

            if (!fate)
                return Failure(region, restored, page, error: null);
            restored++;
        }
        return new JournalRegionRestoreResult
        {
            Region = region.Region,
            RestoredPages = restored,
            TotalPages = region.Pages.Count,
        };
    }

    private static JournalRegionRestoreResult Failure(
        JournalRegionSnapshot region, int restored, JournalPageSnapshot failedPage, Exception? error) =>
        new()
        {
            Region = region.Region,
            RestoredPages = restored,
            TotalPages = region.Pages.Count,
            FailedPage = failedPage,
            Error = error,
        };
}
