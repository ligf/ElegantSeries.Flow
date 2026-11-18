using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Pins the proven gate in the post-call binding for the lagging
/// announcement order: when the applied test is unproven (the landed
/// page's effective parameter threw from its own Equals), the
/// post-call binding must write nothing. A retention-only record
/// written there would pre-empt the settle announcement's
/// bind-if-unbound fill, and the page's real parameter would be lost
/// from every later snapshot.
/// </summary>
public sealed class JournalLaggedAnnouncementBindingTests
{
    [Fact]
    public async Task UnprovenAppliedTest_PostCallWritesNothing_LaggedAnnouncementFillsFromEffectiveParameter()
    {
        var fake = new FakeNavigationService();
        var store = new InMemoryJournalStore();
        var builder = new JournalBuilder().RegisterParameter<ExplodingParam>(
            p => $"id={p.Id}",
            text => new ExplodingParam(int.Parse(text["id=".Length..])));
        using var decorator = new JournalNavigationService(fake, store, builder);
        var journal = new NavigationJournal(decorator, store);

        var errors = new List<Exception>();
        using var _sub = decorator.SubscribeSubscriberError(errors.Add);

        // The page already sits in the region holding ExplodingParam(1);
        // no decorator call has ever bound it.
        var vm = new FakeViewModel();
        fake.Current = vm;
        fake.Stack.Add(vm);
        fake.ParameterOwner = vm;
        fake.ParameterValue = new ExplodingParam(1);
        fake.CurrentRetentionValue = NavigationRetention.KeepAlive;

        // A call passes a different parameter. The fake announces only
        // when told, so the post-call binding runs before any
        // announcement is processed (the lagging order): the applied
        // test compares the page's effective parameter against the
        // passed one, its Equals throws, and the test is unproven —
        // the binding must stay unwritten, the failure is reported
        // once, and the navigation itself is unaffected.
        Assert.True(await decorator.NavigateToAsync<FakeViewModel, ExplodingParam>(new ExplodingParam(2)));
        Assert.Single(await JournalRecordingTests.WaitForEntriesAsync(store, 1));
        Assert.Single(errors);

        // The lagged announcement arrives and is absorbed by the
        // recorded landing's token; its bind-if-unbound fill is the
        // only writer left, and it must find the binding still
        // unwritten so it can fill it from the page's effective
        // parameter. (The fake raises synchronously, so the fill has
        // already run when the raise returns, and absorption appends
        // no entry.)
        fake.RaiseRegionNavigated("MainRegion", vm);
        Assert.Single(await store.ReadEntriesAsync());

        var snapshot = await journal.CaptureSnapshotAsync();
        var region = Assert.Single(snapshot.Regions);
        var page = Assert.Single(region.Pages);
        Assert.Equal(typeof(ExplodingParam).AssemblyQualifiedName, page.ParameterType);
        Assert.Equal("id=1", page.Parameter);
        Assert.Equal(NavigationRetention.KeepAlive, page.Retention);
    }
}
