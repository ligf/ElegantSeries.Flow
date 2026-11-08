using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Snapshot capture and restore tests (contract §4 completion/failure
/// rows and the §5-2 region-abort settlement).
/// </summary>
public class JournalSnapshotRestoreTests
{
    [Fact]
    public async Task Capture_RecordsStacksParametersAndRetentions_SavesToStore()
    {
        var provider = JournalRecordingTests.BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.True(await nav.NavigateToAsync<DetailViewModel, DetailParam>(
            new DetailParam(42), retention: NavigationRetention.KeepAlive));
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>("SideRegion"));

        var snapshot = await journal.CaptureSnapshotAsync();
        Assert.Equal(1, snapshot.FormatVersion);
        Assert.Equal(2, snapshot.Regions.Count);

        var main = snapshot.Regions.Single(r => r.Region == "MainRegion");
        Assert.Equal(2, main.Pages.Count);
        Assert.Equal(typeof(HomeViewModel).AssemblyQualifiedName, main.Pages[0].ViewModelType);
        Assert.Equal(NavigationRetention.Destroy, main.Pages[0].Retention);
        Assert.Equal(typeof(DetailViewModel).AssemblyQualifiedName, main.Pages[1].ViewModelType);
        Assert.Equal("id=42", main.Pages[1].Parameter);
        Assert.Equal(NavigationRetention.KeepAlive, main.Pages[1].Retention);

        var loaded = await journal.LoadSnapshotAsync();
        Assert.NotNull(loaded);
        Assert.Equal(snapshot.CapturedUtc, loaded.CapturedUtc);

        // The store persisted the captured snapshot's content. (Not the
        // same instance: stores isolate the saved snapshot from the
        // caller's object, as the JSON store does via serialization.)
        var stored = await store.LoadSnapshotAsync();
        Assert.NotNull(stored);
        Assert.Equal(snapshot.CapturedUtc, stored.CapturedUtc);
        Assert.Equal(snapshot.Regions.Count, stored.Regions.Count);
        var storedMain = stored.Regions.Single(r => r.Region == "MainRegion");
        Assert.Equal(
            main.Pages.Select(p => (p.ViewModelType, p.Parameter, p.Retention)),
            storedMain.Pages.Select(p => (p.ViewModelType, p.Parameter, p.Retention)));
    }

    [Fact]
    public async Task Capture_CachedPagesAreNotPartOfTheSnapshot()
    {
        var provider = JournalRecordingTests.BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        // SetRoot moves the KeepAlive Home off the stack into the cache;
        // the cache is never captured, so the snapshot holds only the
        // new root page.
        Assert.True(await nav.NavigateToAsync<HomeViewModel>(retention: NavigationRetention.KeepAlive));
        Assert.True(await nav.SetRootAsync<SettingsViewModel>());

        var snapshot = await journal.CaptureSnapshotAsync();
        var region = Assert.Single(snapshot.Regions);
        var page = Assert.Single(region.Pages);
        Assert.Equal(typeof(SettingsViewModel).AssemblyQualifiedName, page.ViewModelType);
    }

    [Fact]
    public async Task Restore_RebuildsStacksWithParameters()
    {
        var param = new DetailParam(9);
        var source = JournalRecordingTests.BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _s = source;
        var sourceNav = source.GetRequiredService<INavigationService>();
        Assert.True(await sourceNav.NavigateToAsync<HomeViewModel>());
        Assert.True(await sourceNav.NavigateToAsync<DetailViewModel, DetailParam>(param));
        var snapshot = await source.GetRequiredService<INavigationJournal>().CaptureSnapshotAsync();

        var target = JournalRecordingTests.BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _t = target;
        var targetNav = target.GetRequiredService<INavigationService>();
        var operations = new List<JournalRestoreOperation>();

        var result = await target.GetRequiredService<INavigationJournal>().RestoreAsync(
            snapshot, page => RestoreDelegate(targetNav, page, operations));

        Assert.True(result.AllSucceeded);
        Assert.Equal(
            [JournalRestoreOperation.Root, JournalRestoreOperation.Push],
            operations);
        var stack = targetNav.GetNavigationStack();
        Assert.Equal(2, stack.Count);
        Assert.IsType<HomeViewModel>(stack[0]);
        var detail = Assert.IsType<DetailViewModel>(stack[1]);
        Assert.Equal(param, detail.Received);
    }

    [Fact]
    public async Task Restore_FailingRegionStops_OtherRegionsContinue()
    {
        var snapshot = new JournalSnapshot
        {
            Regions =
            [
                new JournalRegionSnapshot
                {
                    Region = "A",
                    Pages = [Page(typeof(HomeViewModel))],
                },
                new JournalRegionSnapshot
                {
                    Region = "B",
                    Pages = [Page(typeof(HomeViewModel)), Page(typeof(SettingsViewModel))],
                },
                new JournalRegionSnapshot
                {
                    Region = "C",
                    Pages = [Page(typeof(ProfileViewModel))],
                },
            ],
        };
        var provider = JournalRecordingTests.BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        var result = await provider.GetRequiredService<INavigationJournal>().RestoreAsync(
            snapshot,
            page => page.Region == "B"
                ? Task.FromResult(false)
                : RestoreDelegate(nav, page, null));

        Assert.False(result.AllSucceeded);
        Assert.Equal(3, result.Regions.Count);
        Assert.True(result.Regions[0].Succeeded);
        var failed = result.Regions[1];
        Assert.False(failed.Succeeded);
        Assert.Equal(0, failed.RestoredPages);
        Assert.Equal(2, failed.TotalPages);
        Assert.NotNull(failed.FailedPage);
        Assert.Null(failed.Error);
        Assert.True(result.Regions[2].Succeeded);
        Assert.IsType<ProfileViewModel>(nav.GetCurrentViewModel("C"));
    }

    [Fact]
    public async Task Restore_DelegateThrows_RegionStopsWithError_OthersContinue()
    {
        var snapshot = new JournalSnapshot
        {
            Regions =
            [
                new JournalRegionSnapshot
                {
                    Region = "A",
                    Pages = [Page(typeof(HomeViewModel)), Page(typeof(SettingsViewModel)), Page(typeof(ProfileViewModel))],
                },
                new JournalRegionSnapshot
                {
                    Region = "B",
                    Pages = [Page(typeof(HomeViewModel))],
                },
            ],
        };
        var provider = JournalRecordingTests.BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var calls = 0;

        var result = await provider.GetRequiredService<INavigationJournal>().RestoreAsync(
            snapshot,
            page =>
            {
                calls++;
                if (page.Region == "A" && page.ViewModelType == typeof(SettingsViewModel).AssemblyQualifiedName)
                    throw new InvalidOperationException("delegate boom");
                return RestoreDelegate(nav, page, null);
            });

        var regionA = result.Regions[0];
        Assert.False(regionA.Succeeded);
        Assert.Equal(1, regionA.RestoredPages);
        Assert.Equal("delegate boom", regionA.Error?.Message);
        Assert.True(result.Regions[1].Succeeded);
        // A: Home ok, Settings throws, Profile never attempted. B: 1 call.
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Restore_MissingDeserializer_CountsAsPageFailure_DelegateNotCalled()
    {
        var snapshot = new JournalSnapshot
        {
            Regions =
            [
                new JournalRegionSnapshot
                {
                    Region = "MainRegion",
                    Pages =
                    [
                        new JournalPageSnapshot
                        {
                            ViewModelType = typeof(DetailViewModel).AssemblyQualifiedName!,
                            ParameterType = typeof(UnregisteredParam).AssemblyQualifiedName,
                            Parameter = "id=1",
                        },
                    ],
                },
            ],
        };
        var provider = JournalRecordingTests.BuildProvider();
        using var _p = provider;
        var calls = 0;

        var result = await provider.GetRequiredService<INavigationJournal>().RestoreAsync(
            snapshot,
            page =>
            {
                calls++;
                return Task.FromResult(true);
            });

        Assert.Equal(0, calls);
        var region = Assert.Single(result.Regions);
        Assert.False(region.Succeeded);
        Assert.Null(region.Error);
        Assert.NotNull(region.FailedPage);
    }

    [Fact]
    public async Task Restore_UnsupportedVersion_Throws()
    {
        var provider = JournalRecordingTests.BuildProvider();
        using var _p = provider;
        var journal = provider.GetRequiredService<INavigationJournal>();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            journal.RestoreAsync(
                new JournalSnapshot { FormatVersion = 99 },
                _ => Task.FromResult(true)));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            journal.RestoreAsync(null!, _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Clear_RemovesEntriesAndSnapshot()
    {
        var provider = JournalRecordingTests.BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        await journal.CaptureSnapshotAsync();
        await journal.ClearAsync();

        Assert.Empty(await journal.GetEntriesAsync());
        Assert.Null(await journal.LoadSnapshotAsync());
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static JournalPageSnapshot Page(Type viewModelType) => new()
    {
        ViewModelType = viewModelType.AssemblyQualifiedName!,
    };

    private static async Task<bool> RestoreDelegate(
        INavigationService nav, JournalPageRestore page, List<JournalRestoreOperation>? operations)
    {
        operations?.Add(page.Operation);
        if (page.ViewModelType == typeof(HomeViewModel).AssemblyQualifiedName)
        {
            return page.Operation == JournalRestoreOperation.Root
                ? await nav.SetRootAsync<HomeViewModel>(page.Region, page.Retention)
                : await nav.NavigateToAsync<HomeViewModel>(page.Region, NavigationOperation.Push, page.Retention);
        }
        if (page.ViewModelType == typeof(SettingsViewModel).AssemblyQualifiedName)
        {
            return page.Operation == JournalRestoreOperation.Root
                ? await nav.SetRootAsync<SettingsViewModel>(page.Region, page.Retention)
                : await nav.NavigateToAsync<SettingsViewModel>(page.Region, NavigationOperation.Push, page.Retention);
        }
        if (page.ViewModelType == typeof(ProfileViewModel).AssemblyQualifiedName)
        {
            return page.Operation == JournalRestoreOperation.Root
                ? await nav.SetRootAsync<ProfileViewModel>(page.Region, page.Retention)
                : await nav.NavigateToAsync<ProfileViewModel>(page.Region, NavigationOperation.Push, page.Retention);
        }
        if (page.ViewModelType == typeof(DetailViewModel).AssemblyQualifiedName)
        {
            var parameter = (DetailParam)page.Parameter!;
            return page.Operation == JournalRestoreOperation.Root
                ? await nav.SetRootAsync<DetailViewModel, DetailParam>(parameter, page.Region, page.Retention)
                : await nav.NavigateToAsync<DetailViewModel, DetailParam>(parameter, page.Region, NavigationOperation.Push, page.Retention);
        }
        return false;
    }
}
