namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>Built-in store tests: cap enforcement, snapshot handling,
/// JSON file round-trips across instances.</summary>
public class JournalStoreTests
{
    [Fact]
    public async Task InMemory_CapDropsOldest_AndClearRemovesEverything()
    {
        var store = new InMemoryJournalStore(new JournalOptions { MaxHistoryEntries = 2 });
        await store.AppendAsync(Entry(1));
        await store.AppendAsync(Entry(2));
        await store.AppendAsync(Entry(3));
        await store.SaveSnapshotAsync(new JournalSnapshot());

        var entries = await store.ReadEntriesAsync();
        Assert.Equal([2L, 3L], entries.Select(e => e.Sequence));
        Assert.NotNull(await store.LoadSnapshotAsync());

        await store.ClearAsync();
        Assert.Empty(await store.ReadEntriesAsync());
        Assert.Null(await store.LoadSnapshotAsync());
    }

    [Fact]
    public void InMemory_InvalidCap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryJournalStore(new JournalOptions { MaxHistoryEntries = 0 }));
    }

    [Fact]
    public async Task JsonFile_RoundTripsAcrossInstances()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "journal.json");
            var snapshot = new JournalSnapshot
            {
                CapturedUtc = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero),
                Regions =
                [
                    new JournalRegionSnapshot
                    {
                        Region = "MainRegion",
                        Pages =
                        [
                            new JournalPageSnapshot
                            {
                                ViewModelType = typeof(HomeViewModel).AssemblyQualifiedName!,
                                Retention = Navigation.NavigationRetention.KeepAlive,
                            },
                        ],
                    },
                ],
            };

            using (var store = new JsonFileNavigationJournalStore(path))
            {
                await store.AppendAsync(Entry(1));
                await store.AppendAsync(Entry(2));
                await store.SaveSnapshotAsync(snapshot);
            }

            using var reopened = new JsonFileNavigationJournalStore(path);
            var entries = await reopened.ReadEntriesAsync();
            Assert.Equal([1L, 2L], entries.Select(e => e.Sequence));
            Assert.Equal(JournalEntryKind.Navigated, entries[0].Kind);

            var loaded = await reopened.LoadSnapshotAsync();
            Assert.NotNull(loaded);
            Assert.Equal(snapshot.CapturedUtc, loaded.CapturedUtc);
            var page = Assert.Single(Assert.Single(loaded.Regions).Pages);
            Assert.Equal(typeof(HomeViewModel).AssemblyQualifiedName, page.ViewModelType);
            Assert.Equal(Navigation.NavigationRetention.KeepAlive, page.Retention);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonFile_CapEnforcedOnAppend()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "journal.json");
            using var store = new JsonFileNavigationJournalStore(
                path, new JournalOptions { MaxHistoryEntries = 2 });
            await store.AppendAsync(Entry(1));
            await store.AppendAsync(Entry(2));
            await store.AppendAsync(Entry(3));

            using var reopened = new JsonFileNavigationJournalStore(path);
            var entries = await reopened.ReadEntriesAsync();
            Assert.Equal([2L, 3L], entries.Select(e => e.Sequence));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonFile_FailedAppend_DoesNotResurfaceOnNextAppend()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "journal.json");
            using var store = new JsonFileNavigationJournalStore(path);
            await store.AppendAsync(Entry(1));

            // Block the temp file the writer stages through, so the
            // append of entry 2 fails midway.
            Directory.CreateDirectory(path + ".tmp");
            await Assert.ThrowsAnyAsync<Exception>(async () => await store.AppendAsync(Entry(2)));
            Directory.Delete(path + ".tmp");

            await store.AppendAsync(Entry(3));
            var entries = await store.ReadEntriesAsync();
            Assert.Equal([1L, 3L], entries.Select(e => e.Sequence));

            using var reopened = new JsonFileNavigationJournalStore(path);
            Assert.Equal([1L, 3L], (await reopened.ReadEntriesAsync()).Select(e => e.Sequence));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonFile_FailedClear_KeepsCachedEntries()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "journal.json");
            using var store = new JsonFileNavigationJournalStore(path);
            await store.AppendAsync(Entry(1));

            // Make the snapshot file undeletable (a directory sits at
            // its path), so ClearAsync fails midway.
            var snapshotPath = Path.Combine(directory, "journal.snapshot.json");
            Directory.CreateDirectory(snapshotPath);
            await Assert.ThrowsAnyAsync<Exception>(async () => await store.ClearAsync());
            Directory.Delete(snapshotPath);

            // The failed clear must not have emptied this instance's
            // view of the history.
            var entry = Assert.Single(await store.ReadEntriesAsync());
            Assert.Equal(1L, entry.Sequence);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonFile_CorruptContent_SurfacesAsException()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "journal.json");
            await File.WriteAllTextAsync(path, "this is not json");
            using var store = new JsonFileNavigationJournalStore(path);
            await Assert.ThrowsAnyAsync<Exception>(async () => await store.ReadEntriesAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JournalEntry Entry(long sequence) => new()
    {
        Sequence = sequence,
        TimestampUtc = DateTimeOffset.UtcNow,
        Region = "MainRegion",
        Kind = JournalEntryKind.Navigated,
        ViewModelType = typeof(HomeViewModel).AssemblyQualifiedName,
    };

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "journal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
