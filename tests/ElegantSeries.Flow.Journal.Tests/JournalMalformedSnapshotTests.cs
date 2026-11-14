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
            // The malformed snapshot arrives as a file on disk (the
            // JSON store's save boundary now rejects this shape, so the
            // file is written directly): loading stays lenient, and the
            // restore must reject what deserialization produced.
            var snapshotPath = Path.Combine(directory, "journal.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, "{\"FormatVersion\":1,\"Regions\":null}");
            using var store = new JsonFileNavigationJournalStore(Path.Combine(directory, "journal.json"));
            var loaded = await store.LoadSnapshotAsync();
            Assert.NotNull(loaded);

            var journal = BuildJournal(store);
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => journal.RestoreAsync(loaded!, _ => Task.FromResult(true)));
            Assert.Contains("Regions is null", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Restore_ZeroFormatVersion_ThrowsArgumentException()
    {
        var journal = BuildJournal(new InMemoryJournalStore());
        var snapshot = new JournalSnapshot { FormatVersion = 0, Regions = [] };

        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RestoreAsync(snapshot, _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Restore_NegativeFormatVersion_ThrowsArgumentException()
    {
        var journal = BuildJournal(new InMemoryJournalStore());
        var snapshot = new JournalSnapshot { FormatVersion = -1, Regions = [] };

        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RestoreAsync(snapshot, _ => Task.FromResult(true)));
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
