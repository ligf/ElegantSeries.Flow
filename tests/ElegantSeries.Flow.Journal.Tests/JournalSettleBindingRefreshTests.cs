using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Pins the settle-time binding refresh: a landing attributed to
/// exactly one in-flight call updates the page's binding when its
/// announcement is processed, so a snapshot captured while the call's
/// continuation is still pending pairs the new stack with the NEW
/// parameter — not the previous activation's.
/// </summary>
public class JournalSettleBindingRefreshTests
{
    [Fact]
    public async Task Capture_BetweenAnnouncementAndCallContinuation_ReadsNewParameter()
    {
        var provider = JournalRecordingTests.BuildProvider(journal => journal.RegisterParameter<DetailParam>(
            p => $"id={p.Id}",
            text => new DetailParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();
        var decorator = (JournalNavigationService)nav;

        // Detail lands with id=1 (kept alive) and is covered by Home.
        Assert.True(await nav.NavigateToAsync<VoteDetailViewModel, DetailParam>(
            new DetailParam(1), retention: NavigationRetention.KeepAlive));
        Assert.True(await nav.NavigateToAsync<HomeViewModel>());

        // The reactivation's announcement is observed through the
        // decorator's republished event, which is raised after the
        // recording tap (settle processing) has run.
        var announced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        nav.RegionNavigated += (region, vm) =>
        {
            if (vm is VoteDetailViewModel)
                announced.TrySetResult();
        };

        // Hold the reactivation call's continuation open after the
        // wrapped call succeeds, before its post-call binding runs.
        var hookEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        decorator.AfterInnerNavigationHook = async () =>
        {
            hookEntered.TrySetResult();
            await releaseHook.Task;
        };

        // Pop-to-existing reactivation with a new parameter: the
        // existing entry is claimed by the page's INavigationTargetAware
        // vote (a plain parameter-inequality would push a new page).
        var navigation = nav.NavigateToAsync<VoteDetailViewModel, DetailParam>(new DetailParam(2));
        await hookEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await announced.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The stack already shows the reactivated page alone; its
        // binding must already carry the new parameter.
        var snapshot = await journal.CaptureSnapshotAsync();
        var region = Assert.Single(snapshot.Regions);
        var page = Assert.Single(region.Pages);
        Assert.Equal(typeof(VoteDetailViewModel).AssemblyQualifiedName, page.ViewModelType);
        Assert.Equal("id=2", page.Parameter);

        releaseHook.TrySetResult();
        Assert.True(await navigation.WaitAsync(TimeSpan.FromSeconds(10)));
        decorator.AfterInnerNavigationHook = null;
    }

    [Fact]
    public async Task SettleRefresh_ParameterEqualsThrows_AnnouncementStillCompletes()
    {
        var provider = JournalRecordingTests.BuildProvider(journal => journal.RegisterParameter<ExplodingParam>(
            p => $"id={p.Id}",
            text => new ExplodingParam(int.Parse(text["id=".Length..]))));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        var errors = new List<Exception>();
        nav.SubscriberError += errors.Add;
        var landed = 0;
        nav.RegionNavigated += (region, vm) =>
        {
            if (vm is ExplodingViewModel)
                landed++;
        };

        // The page lands (claimed by vote) even though its parameter's
        // Equals throws: the wrapped classification guards it, and the
        // decorator's settle refresh must guard its own Equals call
        // the same way. Each call still throws from the post-call
        // binding on the call flow — that is the pre-existing
        // call-flow behavior; what must survive is the announcement's
        // remaining processing.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.NavigateToAsync<ExplodingViewModel, ExplodingParam>(
                new ExplodingParam(1), retention: NavigationRetention.KeepAlive));
        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.NavigateToAsync<ExplodingViewModel, ExplodingParam>(new ExplodingParam(2)));

        // Announcements may lag the calls' completions, so poll for
        // both landings (and both error reports) before asserting.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (landed < 2 || errors.Count < 2)
        {
            if (DateTime.UtcNow > deadline)
                break;
            await Task.Delay(20);
        }

        // Both announcements completed: the republication reached the
        // app handlers, and the throwing Equals was reported exactly
        // once per landing instead of aborting the tap.
        Assert.Equal(2, landed);
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal("equals exploded (test double)", e.Message));

        // The first landing's binding was still filled (by
        // BindIfUnbound freezing the effective parameter): the guard
        // skipped only the refresh, not the rest of the settle.
        var snapshot = await journal.CaptureSnapshotAsync();
        var region = Assert.Single(snapshot.Regions);
        var page = Assert.Single(region.Pages);
        Assert.Equal(typeof(ExplodingViewModel).AssemblyQualifiedName, page.ViewModelType);
        Assert.Equal("id=1", page.Parameter);
    }

    [Fact]
    public async Task SettleRefresh_ReadBackRetentionNull_FallsBackToCallRetention()
    {
        var inner = new FakeNavigationService();
        var page = new FakeViewModel();
        inner.Current = page;
        inner.ParameterOwner = page;
        inner.ParameterValue = null;
        inner.AllStacksValue = new Dictionary<string, IReadOnlyList<INavigationViewModel>>
        {
            ["MainRegion"] = [page],
        };
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(inner, store, new JournalBuilder());
        var journal = new NavigationJournal(decorator, store);

        // While the forward call is in flight, stage its landing with
        // a null retention read-back, then hide the page from the
        // call's continuation: the continuation defers its binding
        // (AddDeferredLanding) instead of applying one, so the final
        // binding can only have come from the settle-time refresh.
        inner.NavigateHook = () =>
        {
            inner.CurrentRetentionValue = null;
            inner.RaiseRegionNavigated("MainRegion", page);
            inner.Current = null;
        };

        Assert.True(await decorator.NavigateToAsync<FakeViewModel>(
            retention: NavigationRetention.KeepAlive));

        var snapshot = await journal.CaptureSnapshotAsync();
        var region = Assert.Single(snapshot.Regions);
        var captured = Assert.Single(region.Pages);
        Assert.Equal(typeof(FakeViewModel).AssemblyQualifiedName, captured.ViewModelType);
        Assert.Equal(NavigationRetention.KeepAlive, captured.Retention);
    }
}
