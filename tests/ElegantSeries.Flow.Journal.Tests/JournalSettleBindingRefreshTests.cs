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
}
