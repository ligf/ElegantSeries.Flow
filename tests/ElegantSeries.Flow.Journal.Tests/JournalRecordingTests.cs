using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Recording-contract tests for the journal decorator, against a real
/// <see cref="INavigationService"/>: each test maps to a row of the
/// contract table in <c>files/journal-design.md</c> §4.
/// </summary>
public class JournalRecordingTests
{
    // ── Completion semantics ────────────────────────────────────────

    [Fact]
    public async Task SuccessfulNavigation_EntryAppendedBeforeCompletion()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());

        var entries = await store.ReadEntriesAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(JournalEntryKind.Navigated, entry.Kind);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal("MainRegion", entry.Region);
        Assert.Equal(typeof(HomeViewModel).AssemblyQualifiedName, entry.ViewModelType);
        Assert.Null(entry.ParameterType);
        Assert.Null(entry.ParameterSnapshot);
    }

    [Fact]
    public async Task FailedNavigation_RecordsNothing()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        // GoBack at the root fails; a guard-vetoed departure fails.
        Assert.False(await nav.GoBackAsync());
        Assert.True(await nav.NavigateToAsync<GuardedViewModel>());
        Assert.False(await nav.NavigateToAsync<HomeViewModel>());

        var entries = await store.ReadEntriesAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(typeof(GuardedViewModel).AssemblyQualifiedName, entry.ViewModelType);
    }

    [Fact]
    public async Task BackNavigation_RecordsBackWithLandedType()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>());
        Assert.True(await nav.GoBackAsync());

        // The popped page's release is recorded inside the GoBack call,
        // causally before the Back entry itself.
        var entries = await WaitForEntriesAsync(store, 4);
        Assert.Equal(4, entries.Count);
        Assert.Equal(JournalEntryKind.Released, entries[2].Kind);
        Assert.Equal(typeof(SettingsViewModel).AssemblyQualifiedName, entries[2].ViewModelType);
        Assert.Equal(JournalEntryKind.Back, entries[3].Kind);
        Assert.Equal(typeof(HomeViewModel).AssemblyQualifiedName, entries[3].ViewModelType);
        Assert.Equal([1L, 2L, 3L, 4L], entries.Select(e => e.Sequence));
    }

    // ── Parameter snapshots ─────────────────────────────────────────

    [Fact]
    public async Task RegisteredParameter_SnapshotFrozenAtCallTime()
    {
        var provider = BuildProvider(journal => journal.RegisterParameter<MutableParam>(
            p => "value=" + p.Value,
            text => new MutableParam { Value = text["value=".Length..] }));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        var parameter = new MutableParam { Value = "before" };
        Assert.True(await nav.NavigateToAsync<MutableDetailViewModel, MutableParam>(parameter));
        parameter.Value = "after";

        var entry = Assert.Single(await store.ReadEntriesAsync());
        Assert.Equal(typeof(MutableParam).AssemblyQualifiedName, entry.ParameterType);
        Assert.Equal("value=before", entry.ParameterSnapshot);
    }

    [Fact]
    public async Task UnregisteredParameter_RecordsTypeWithNullSnapshot()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(7)));

        var entry = Assert.Single(await store.ReadEntriesAsync());
        Assert.Equal(typeof(DetailParam).AssemblyQualifiedName, entry.ParameterType);
        Assert.Null(entry.ParameterSnapshot);
    }

    // ── Failure isolation: recording never touches the navigation ───

    [Fact]
    public async Task ThrowingSerializer_NavigationUnaffected_EntryHasNullSnapshot_ErrorReported()
    {
        var provider = BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            _ => throw new InvalidOperationException("serializer boom"),
            _ => new DetailParam(0)));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();
        var errors = new List<Exception>();
        using var _sub = nav.SubscribeSubscriberError(errors.Add);

        Assert.True(await nav.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(3)));

        var entry = Assert.Single(await store.ReadEntriesAsync());
        Assert.Null(entry.ParameterSnapshot);
        var error = Assert.Single(errors);
        Assert.Equal("serializer boom", error.Message);
    }

    [Fact]
    public async Task ThrowingStore_NavigationUnaffected_ErrorReported()
    {
        var provider = BuildProvider(journal => journal.UseStore(new ThrowingStore()));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var errors = new List<Exception>();
        using var _sub = nav.SubscribeSubscriberError(errors.Add);

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel());

        var error = Assert.Single(errors);
        Assert.Equal("store is down", error.Message);
    }

    // ── Queued (best-effort) entry kinds ────────────────────────────

    [Fact]
    public async Task ReleasedPage_RecordsReleasedEntry()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.True(await nav.SetRootAsync<SettingsViewModel>());

        // Causal order is deterministic: the release happens inside the
        // second navigation, before its own Navigated entry is recorded.
        var entries = await WaitForEntriesAsync(store, 3);
        Assert.Equal(3, entries.Count);
        Assert.Equal(JournalEntryKind.Navigated, entries[0].Kind);
        Assert.Equal(JournalEntryKind.Released, entries[1].Kind);
        Assert.Equal(typeof(HomeViewModel).AssemblyQualifiedName, entries[1].ViewModelType);
        Assert.Equal(JournalEntryKind.Navigated, entries[2].Kind);
        Assert.Equal(typeof(SettingsViewModel).AssemblyQualifiedName, entries[2].ViewModelType);
    }

    [Fact]
    public async Task Remove_RecordsRemovedEntry()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>());
        Assert.Equal(1, nav.Remove<HomeViewModel>());

        // Removing a Destroy page releases it inside the Remove call;
        // the Removed entry follows causally after that release.
        var entries = await WaitForKindAsync(store, JournalEntryKind.Removed);
        Assert.Equal(JournalEntryKind.Removed, entries[^1].Kind);
        Assert.Equal(typeof(HomeViewModel).AssemblyQualifiedName, entries[^1].ViewModelType);
        Assert.Contains(entries, e =>
            e.Kind == JournalEntryKind.Released &&
            e.ViewModelType == typeof(HomeViewModel).AssemblyQualifiedName);
    }

    [Fact]
    public async Task ClearCacheAsync_CompletionCoversTheCacheClearedEntry()
    {
        // Deterministic flush proof: the store blocks the CacheCleared
        // append, so the clear call can only complete if its completion
        // genuinely waits for the queued entry to land.
        var store = new BlockableStore();
        var provider = BuildProvider(journal => journal.UseStore(store));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>(
            retention: NavigationRetention.KeepAlive));
        // Replace moves the KeepAlive Home off the stack into the cache.
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>(
            operation: NavigationOperation.Replace));

        var clear = nav.ClearCacheAsync("MainRegion");
        await store.CacheAppendStarted;
        Assert.False(clear.IsCompleted);
        store.ReleaseCacheAppend();
        await clear;

        var entries = await store.ReadEntriesAsync();
        Assert.Contains(entries, e => e.Kind == JournalEntryKind.CacheCleared);
    }

    [Fact]
    public async Task ClearCache_Sync_RecordsCacheClearedEntryViaQueue()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>(
            operation: NavigationOperation.Replace));
        nav.ClearCache("MainRegion");

        var entries = await WaitForKindAsync(store, JournalEntryKind.CacheCleared);
        Assert.Contains(entries, e => e.Kind == JournalEntryKind.CacheCleared);
    }

    [Fact]
    public async Task NoOpClearCache_RecordsNothing()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        await nav.ClearCacheAsync("MainRegion");

        Assert.Empty(await store.ReadEntriesAsync());
    }

    // ── History cap ─────────────────────────────────────────────────

    [Fact]
    public async Task HistoryCap_DropsOldestEntries()
    {
        var provider = BuildProvider(journal => journal.Options.MaxHistoryEntries = 3);
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        foreach (var region in new[] { "R1", "R2", "R3", "R4", "R5" })
            Assert.True(await nav.NavigateToAsync<HomeViewModel>(region));

        var entries = await store.ReadEntriesAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal([3L, 4L, 5L], entries.Select(e => e.Sequence));
        Assert.Equal(["R3", "R4", "R5"], entries.Select(e => e.Region));
    }

    // ── Snapshot binding discipline ─────────────────────────────────

    [Fact]
    public async Task SelfTargetNoOp_KeepsOriginalParameterBinding()
    {
        var provider = BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        Assert.True(await nav.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(1)));
        // Same type + Push + Destroy + no refresh is a Core no-op: the
        // call succeeds WITHOUT applying the new parameter.
        Assert.True(await nav.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(2)));
        var detail = Assert.IsType<DetailViewModel>(nav.GetCurrentViewModel());
        Assert.Equal(1, detail.Received?.Id);

        var snapshot = await journal.CaptureSnapshotAsync();
        var page = Assert.Single(Assert.Single(snapshot.Regions).Pages);
        Assert.Equal("id=1", page.Parameter);
    }

    [Fact]
    public async Task PopToExistingClaim_KeepsOriginalRetention()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>(retention: NavigationRetention.KeepAlive));
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>());
        // Home is in the stack as a KeepAlive entry: this Push claims
        // (reactivates) it and pops Settings. The entry keeps its
        // original KeepAlive retention even though the call says Destroy.
        Assert.True(await nav.NavigateToAsync<HomeViewModel>(retention: NavigationRetention.Destroy));

        var snapshot = await journal.CaptureSnapshotAsync();
        var page = Assert.Single(Assert.Single(snapshot.Regions).Pages);
        Assert.Equal(typeof(HomeViewModel).AssemblyQualifiedName, page.ViewModelType);
        Assert.Equal(NavigationRetention.KeepAlive, page.Retention);
    }

    [Fact]
    public async Task GoBackToWithParameter_UpdatesLandedPageBinding()
    {
        var provider = BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        // The vote-claimed GoBackTo is the shape that delivers a NEW
        // parameter to an existing page (a parameter that matches
        // nothing and is not claimed selects no page at all).
        Assert.True(await nav.NavigateToAsync<VoteDetailViewModel, DetailParam>(new DetailParam(1)));
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>());
        Assert.True(await nav.GoBackToAsync<VoteDetailViewModel, DetailParam>(new DetailParam(2)));
        var detail = Assert.IsType<VoteDetailViewModel>(nav.GetCurrentViewModel());
        Assert.Equal(2, detail.Received?.Id);

        var snapshot = await journal.CaptureSnapshotAsync();
        var page = Assert.Single(Assert.Single(snapshot.Regions).Pages);
        Assert.Equal("id=2", page.Parameter);
    }

    [Fact]
    public async Task GoBackToWithParameter_RecordsParameterOnBackEntry()
    {
        var provider = BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<VoteDetailViewModel, DetailParam>(new DetailParam(1)));
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>());
        Assert.True(await nav.GoBackToAsync<VoteDetailViewModel, DetailParam>(new DetailParam(2)));

        var entries = await store.ReadEntriesAsync();
        var back = Assert.Single(entries, e => e.Kind == JournalEntryKind.Back);
        Assert.Equal(typeof(DetailParam).AssemblyQualifiedName, back.ParameterType);
        Assert.Equal("id=2", back.ParameterSnapshot);
    }

    [Fact]
    public async Task BypassedNavigation_ViaAttachedNavigationProperty_RecordedAtSettle()
    {
        var provider = BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<CallbackViewModel>());

        // The callback navigated to Detail(5) through the attached
        // Navigation property (the wrapped service itself): the journal
        // never saw the call, so the landing is recorded from its
        // settle announcement.
        await WaitForCurrentAsync<DetailViewModel>(nav);
        var entries = await WaitForEntriesAsync(store, 2);
        Assert.Contains(entries, e =>
            e.Kind == JournalEntryKind.Navigated &&
            e.ViewModelType == typeof(DetailViewModel).AssemblyQualifiedName &&
            e.ParameterSnapshot == "id=5");

        var snapshot = await journal.CaptureSnapshotAsync();
        var pages = Assert.Single(snapshot.Regions).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Equal(typeof(CallbackViewModel).AssemblyQualifiedName, pages[0].ViewModelType);
        Assert.Null(pages[0].Parameter);
        Assert.Equal(typeof(DetailViewModel).AssemblyQualifiedName, pages[1].ViewModelType);
        Assert.Equal("id=5", pages[1].Parameter);
    }

    [Fact]
    public async Task CallbackRegisteredNavigation_ThroughInjectedService_BindsWhenPageSettles()
    {
        var provider = BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        Assert.True(await nav.NavigateToAsync<InjectedCallbackViewModel>());
        var callback = Assert.IsType<InjectedCallbackViewModel>(nav.GetNavigationStack()[0]);
        Assert.IsType<JournalNavigationService>(callback.InjectedService);

        // The callback registered a chained navigation to Detail(7)
        // through the decorator: accepted before the page exists, bound
        // when the page settles.
        await WaitForCurrentAsync<DetailViewModel>(nav);

        var store = provider.GetRequiredService<INavigationJournalStore>();
        var entries = await WaitForEntriesAsync(store, 2);
        Assert.Contains(entries, e =>
            e.Kind == JournalEntryKind.Navigated &&
            e.ViewModelType == typeof(DetailViewModel).AssemblyQualifiedName &&
            e.ParameterSnapshot == "id=7");

        var snapshot = await journal.CaptureSnapshotAsync();
        var pages = Assert.Single(snapshot.Regions).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Equal(typeof(DetailViewModel).AssemblyQualifiedName, pages[1].ViewModelType);
        Assert.Equal("id=7", pages[1].Parameter);
    }

    private static async Task WaitForCurrentAsync<TViewModel>(INavigationService nav)
        where TViewModel : INavigationViewModel
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (nav.GetCurrentViewModel() is not TViewModel)
        {
            Assert.True(DateTime.UtcNow < deadline, "chained navigation never landed");
            await Task.Delay(20);
        }
    }

    // ── Concurrency: sequence order ─────────────────────────────────

    [Fact]
    public async Task ParallelNavigations_StoreOrderMatchesSequenceOrder()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        var regions = new[] { "P1", "P2", "P3", "P4" };
        await Task.WhenAll(regions.Select(region => nav.NavigateToAsync<HomeViewModel>(region)));

        var entries = await store.ReadEntriesAsync();
        Assert.Equal(4, entries.Count);
        Assert.Equal([1L, 2L, 3L, 4L], entries.Select(e => e.Sequence));
        Assert.Equal(
            regions.OrderBy(r => r),
            entries.Select(e => e.Region).OrderBy(r => r));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    internal static ServiceProvider BuildProvider(Action<JournalBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<DetailViewModel>();
        services.AddTransient<MutableDetailViewModel>();
        services.AddTransient<CallbackViewModel>();
        services.AddTransient<InjectedCallbackViewModel>();
        services.AddTransient<DisposeAttemptingViewModel>();
        services.AddTransient<VoteDetailViewModel>();
        services.AddTransient<GuardedViewModel>();
        services.AddElegantSeriesFlowJournal(configure);
        return services.BuildServiceProvider();
    }

    internal static async Task<IReadOnlyList<JournalEntry>> WaitForEntriesAsync(
        INavigationJournalStore store, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            var entries = await store.ReadEntriesAsync();
            if (entries.Count >= count || DateTime.UtcNow > deadline)
                return entries;
            await Task.Delay(20);
        }
    }

    internal static async Task<IReadOnlyList<JournalEntry>> WaitForKindAsync(
        INavigationJournalStore store, JournalEntryKind kind)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            var entries = await store.ReadEntriesAsync();
            if (entries.Any(e => e.Kind == kind) || DateTime.UtcNow > deadline)
                return entries;
            await Task.Delay(20);
        }
    }
}
