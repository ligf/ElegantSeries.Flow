using ElegantSeries.Flow.Hosting;
using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Avalonia.Tests;

/// <summary>
/// Tests for the shared <see cref="NavigationHostController{TView}"/>
/// (<c>object</c> stands in for the view type), mirroring the WPF.Tests
/// Logic coverage of the current-page staleness rule: when the
/// service's current-page answer is definitive
/// (<see cref="IDefinitiveCurrentPageQuery"/>), an event whose page is
/// no longer current — including the region having lost its page
/// entirely — is stale and must not be applied; any other
/// implementation keeps the compatibility rule that a null answer
/// means "unknown" and never marks an event stale.
/// </summary>
public sealed class NavigationHostControllerTests
{
    /// <summary>
    /// Fake whose current-page answer carries the built-in service's
    /// definitive guarantee, so at execution time a null answer means
    /// "the region has no current page", not "unknown".
    /// </summary>
    private sealed class DefinitiveFakeNavigationService : FakeNavigationService, IDefinitiveCurrentPageQuery
    {
        INavigationViewModel? IDefinitiveCurrentPageQuery.GetDefinitiveCurrentViewModel(string regionName)
            => GetCurrentViewModel(regionName);
    }

    private static NavigationHostController<object> CreateController(
        FakeDispatcher dispatcher,
        List<(object View, INavigationViewModel ViewModel)> shown) =>
        new(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { },
            onViewCreationFailed: (_, _) => { });

    [Fact]
    public void QueuedCallback_WithStaleViewModel_SkippedAtExecutionTime()
    {
        // The staleness check at event time passes, but by the time the
        // queued dispatcher callback runs, navigation has moved to
        // another ViewModel. The execution-time re-check must skip the
        // stale callback.
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        controller.Attach(service);

        var vmA = new TestViewModel();
        var vmB = new TestViewModel();
        service.CurrentByRegion["MainRegion"] = vmA;
        service.RaiseRegionNavigated("MainRegion", vmA); // passes check, queued

        // Navigation moves on before the queue runs.
        service.CurrentByRegion["MainRegion"] = vmB;

        dispatcher.RunPosted();

        Assert.Empty(shown); // stale VM-A must not be applied
    }

    [Fact]
    public void QueuedCallback_WhenDefinitiveCurrentBecomesNull_SkippedAtExecutionTime()
    {
        // Same queueing shape as the stale-ViewModel test, but the
        // region ends up with NO current page while the event is
        // queued — the definitive null (for the built-in service: the
        // stack emptied, or the service was disposed and its pages
        // released). Applying the event would resurrect the released
        // page's view.
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new DefinitiveFakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        controller.Attach(service);

        var vmA = new TestViewModel();
        service.CurrentByRegion["MainRegion"] = vmA;
        service.RaiseRegionNavigated("MainRegion", vmA); // passes checks, queued

        // The region loses its current page before the queue runs.
        service.CurrentByRegion.Remove("MainRegion");

        dispatcher.RunPosted();

        Assert.Empty(shown); // the released page must not be resurrected
    }

    [Fact]
    public void QueuedCallback_WhenUnknownCurrentBecomesNull_StillAppliedAtExecutionTime()
    {
        // Compatibility rule for implementations without the definitive
        // guarantee: their null means "unknown", so the queued event is
        // applied exactly as before.
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        controller.Attach(service);

        var vmA = new TestViewModel();
        service.CurrentByRegion["MainRegion"] = vmA;
        service.RaiseRegionNavigated("MainRegion", vmA); // passes checks, queued

        service.CurrentByRegion.Remove("MainRegion");

        dispatcher.RunPosted();

        var shownEntry = Assert.Single(shown);
        Assert.Same(vmA, shownEntry.ViewModel);
    }

    [Fact]
    public void SyncCallback_WhenDefinitiveCurrentIsNull_SkippedAtReceiptTime()
    {
        // The dispatcher has access, so the event is applied
        // synchronously — no queue, no execution-time re-check. The
        // handler observes a definitive null: an earlier subscriber of
        // the same event disposed the service mid-dispatch, emptying
        // the region before this handler ran. Applying the event would
        // resurrect the released page's view, so the receipt-time
        // check must judge it stale, exactly as the queued path's
        // execution-time re-check does.
        var dispatcher = new FakeDispatcher { IsOnUiThread = true };
        var service = new DefinitiveFakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        controller.Attach(service);

        var vmA = new TestViewModel();
        // CurrentByRegion has no entry for MainRegion: the handler
        // observes the region after it lost its page.
        service.RaiseRegionNavigated("MainRegion", vmA);

        Assert.Empty(shown); // the released page must not be resurrected
    }

    [Fact]
    public void SyncCallback_WhenDefinitiveCurrentIsEventViewModel_AppliedAtReceiptTime()
    {
        // Happy path for a definitive service on the synchronous path:
        // the current page IS the event's ViewModel, so the event is
        // applied — the definitive check must only drop events that no
        // longer name the current page, never live ones.
        var dispatcher = new FakeDispatcher { IsOnUiThread = true };
        var service = new DefinitiveFakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        controller.Attach(service);

        var vmA = new TestViewModel();
        service.CurrentByRegion["MainRegion"] = vmA;
        service.RaiseRegionNavigated("MainRegion", vmA);

        var shownEntry = Assert.Single(shown);
        Assert.Same(vmA, shownEntry.ViewModel);
    }

    [Fact]
    public void SyncCallback_WhenUnknownCurrentIsNull_StillAppliedAtReceiptTime()
    {
        // Compatibility rule on the synchronous path: for
        // implementations without the definitive guarantee, a null
        // current means "unknown", so the event is applied exactly as
        // before.
        var dispatcher = new FakeDispatcher { IsOnUiThread = true };
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        controller.Attach(service);

        var vmA = new TestViewModel();
        // CurrentByRegion has no entry for MainRegion.
        service.RaiseRegionNavigated("MainRegion", vmA);

        var shownEntry = Assert.Single(shown);
        Assert.Same(vmA, shownEntry.ViewModel);
    }

    [Fact]
    public void SnapshotDeliveryAfterDetach_AppliesNothing()
    {
        // An event dispatch iterates a snapshot of its invocation list:
        // when an earlier subscriber detaches the host mid-dispatch, the
        // controller's handler still runs — against a detached host whose
        // service reference is already null. It must apply nothing.
        var dispatcher = new FakeDispatcher { IsOnUiThread = true };
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        using var detacher = service.SubscribeRegionNavigated((_, _) => controller.Detach());
        controller.Attach(service);

        service.RaiseRegionNavigated("MainRegion", new TestViewModel());

        Assert.Empty(shown);
    }

    [Fact]
    public void SnapshotDeliveryAfterDetach_QueuesNothing()
    {
        // The queued path has the same hole without the receipt-time
        // guard: the captured service and the execution-time service are
        // both null after a mid-dispatch detach, so the detachment
        // re-check passes vacuously. Nothing may even be queued.
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = CreateController(dispatcher, shown);
        using var detacher = service.SubscribeRegionNavigated((_, _) => controller.Detach());
        controller.Attach(service);

        service.RaiseRegionNavigated("MainRegion", new TestViewModel());

        Assert.Empty(dispatcher.PostedActions);
        dispatcher.RunPosted();
        Assert.Empty(shown);
    }
}
