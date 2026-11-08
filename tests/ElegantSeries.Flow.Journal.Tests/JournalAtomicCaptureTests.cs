using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Contract tests for the snapshot capture's read shape (design doc
/// <c>p2-2-atomic-snapshot-design.md</c> §3): capture takes the region set
/// and every region's stack from ONE atomic
/// <see cref="INavigationService.GetAllNavigationStacks"/> read — never a
/// per-region loop — so the picture is a state that actually existed, and
/// regions the decorator itself never saw (navigated by code holding the
/// wrapped service directly) are captured too.
/// </summary>
public class JournalAtomicCaptureTests
{
    [Fact]
    public async Task CaptureReadsAllStacksExactlyOnceAndNeverPerRegion()
    {
        var inner = new FakeNavigationService();
        var home = new FakeViewModel();
        inner.AllStacksValue = new Dictionary<string, IReadOnlyList<INavigationViewModel>>
        {
            ["MainRegion"] = [home],
        };
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(inner, store, new JournalBuilder());
        var journal = new NavigationJournal(decorator, store);

        var before = DateTimeOffset.UtcNow;
        var snapshot = await journal.CaptureSnapshotAsync();
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(1, inner.GetAllNavigationStacksCallCount);
        Assert.Equal(0, inner.GetNavigationStackCallCount);

        var region = Assert.Single(snapshot.Regions);
        Assert.Equal("MainRegion", region.Region);
        var page = Assert.Single(region.Pages);
        Assert.Equal(typeof(FakeViewModel).AssemblyQualifiedName, page.ViewModelType);
        Assert.InRange(snapshot.CapturedUtc, before, after);
    }

    [Fact]
    public async Task CaptureIncludesRegionsTheDecoratorNeverSaw_WithUnboundDefaults()
    {
        var inner = new FakeNavigationService();
        inner.AllStacksValue = new Dictionary<string, IReadOnlyList<INavigationViewModel>>
        {
            ["MainRegion"] = [new FakeViewModel()],
            // Navigated entirely around the decorator: the decorator
            // never tracked this region, yet the atomic read reports it.
            ["BypassRegion"] = [new FakeViewModel()],
            // An empty stack contributes no region entry, as before.
            ["EmptyRegion"] = [],
        };
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(inner, store, new JournalBuilder());
        var journal = new NavigationJournal(decorator, store);

        var snapshot = await journal.CaptureSnapshotAsync();

        Assert.Equal(2, snapshot.Regions.Count);
        var bypass = Assert.Single(snapshot.Regions, r => r.Region == "BypassRegion");
        var page = Assert.Single(bypass.Pages);
        Assert.Null(page.Parameter);
        Assert.Null(page.ParameterType);
        Assert.Equal(NavigationRetention.Destroy, page.Retention);
        Assert.DoesNotContain(snapshot.Regions, r => r.Region == "EmptyRegion");
    }
}
