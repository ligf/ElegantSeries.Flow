namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Contract tests for <see cref="INavigationJournal.RestoreAsync"/>'s
/// malformed-snapshot surface: a structurally incomplete snapshot
/// (null regions list or null pages list, whether hand-built or
/// deserialized from a persisted file) is rejected with
/// <see cref="ArgumentException"/>, per the documented exception
/// surface — never an escaping <see cref="NullReferenceException"/>.
/// </summary>
public class JournalMalformedSnapshotTests
{
    [Fact]
    public async Task Restore_NullRegions_ThrowsArgumentException()
    {
        var journal = BuildJournal(new InMemoryJournalStore());
        var snapshot = new JournalSnapshot { Regions = null! };

        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RestoreAsync(snapshot, _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Restore_NullPages_ThrowsArgumentException()
    {
        var journal = BuildJournal(new InMemoryJournalStore());
        var snapshot = new JournalSnapshot
        {
            Regions = [new JournalRegionSnapshot { Region = "MainRegion", Pages = null! }],
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RestoreAsync(snapshot, _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Restore_NullPageElement_ThrowsArgumentException()
    {
        var journal = BuildJournal(new InMemoryJournalStore());
        var snapshot = new JournalSnapshot
        {
            Regions =
            [
                new JournalRegionSnapshot
                {
                    Region = "MainRegion",
                    Pages = [null!],
                },
            ],
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RestoreAsync(snapshot, _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Restore_PersistedNullRegions_ThrowsArgumentException()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var store = new JsonFileNavigationJournalStore(Path.Combine(directory, "journal.json"));
            await store.SaveSnapshotAsync(new JournalSnapshot { Regions = null! });
            var loaded = await store.LoadSnapshotAsync();
            Assert.NotNull(loaded);

            var journal = BuildJournal(store);
            await Assert.ThrowsAsync<ArgumentException>(
                () => journal.RestoreAsync(loaded!, _ => Task.FromResult(true)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static NavigationJournal BuildJournal(INavigationJournalStore store) =>
        new(new JournalNavigationService(new FakeNavigationService(), store, new JournalBuilder()), store);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "journal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
