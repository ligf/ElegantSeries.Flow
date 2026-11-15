namespace ElegantSeries.Flow.Journal;

/// <summary>
/// The structural validation shared by every boundary that accepts a
/// <see cref="JournalSnapshot"/> as input — both stores' save paths and
/// <c>NavigationJournal.RestoreAsync</c>: a structurally incomplete
/// snapshot (null <see cref="JournalSnapshot.Regions"/>, a null region
/// or its pages, a null page) is malformed input, rejected with
/// <see cref="ArgumentException"/> instead of escaping later as a
/// <see cref="NullReferenceException"/>.
/// </summary>
internal static class JournalSnapshotValidation
{
    public static void ThrowIfMalformed(JournalSnapshot snapshot)
    {
        if (snapshot.Regions is null)
            throw new ArgumentException("The snapshot is malformed: Regions is null.", nameof(snapshot));
        foreach (var region in snapshot.Regions)
        {
            if (region is null || region.Pages is null)
                throw new ArgumentException("The snapshot is malformed: a region or its Pages is null.", nameof(snapshot));
            foreach (var page in region.Pages)
            {
                if (page is null)
                    throw new ArgumentException("The snapshot is malformed: a page is null.", nameof(snapshot));
            }
        }
    }
}
