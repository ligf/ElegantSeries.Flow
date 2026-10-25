using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;
using ElegantSeries.Flow.Hosting;
using ElegantSeries.Flow.Locating;
using ElegantSeries.Flow.WPF.Tests;

namespace ElegantSeries.Flow.WPF.Tests.Logic;

/// <summary>
/// Linux-runnable tests for <see cref="NavigationHostController{TView}"/> (compiled
/// from the library source; <c>object</c> stands in for the view type). These cover
/// the region filtering, dispatcher marshaling, view caching, and failure handling
/// that <see cref="NavigationHost"/> relies on.
/// </summary>
public sealed class NavigationHostControllerTests
{
    private sealed class Harness
    {
        public FakeDispatcher Dispatcher { get; } = new FakeDispatcher(hasAccess: true);
        public FakeNavigationService Service { get; } = new FakeNavigationService();
        public string RegionName = "MainRegion";
        public Func<INavigationViewModel, object> ViewFactory = _ => new object();
        public List<(object View, INavigationViewModel ViewModel)> Shown { get; } = new();
        public List<(INavigationViewModel ViewModel, Exception Error)> Failures { get; } = new();
        public int ClearedCount { get; private set; }
        public NavigationHostController<object> Controller { get; }

        public Harness()
        {
            Controller = new NavigationHostController<object>(
                Dispatcher,
                regionNameProvider: () => RegionName,
                viewFactory: ctx => ViewFactory(ctx.ViewModel),
                showView: (view, vm) => Shown.Add((view, vm)),
                clearView: () => ClearedCount++,
                onViewCreationFailed: (vm, ex) => Failures.Add((vm, ex)));
        }

        public void Attach() => Controller.Attach(Service);
    }

    [Fact]
    public void RegionMismatch_Ignored()
    {
        var harness = new Harness();
        harness.Attach();
        int factoryCalls = 0;
        harness.ViewFactory = _ => { factoryCalls++; return new object(); };

        harness.Service.RaiseNavigated("OtherRegion", new StubViewModel());

        Assert.Equal(0, factoryCalls);
        Assert.Empty(harness.Shown);
    }

    [Fact]
    public void RegionMatch_CreatesViewAndShows()
    {
        var harness = new Harness();
        harness.Attach();
        var vm = new StubViewModel();
        var view = new object();
        harness.ViewFactory = _ => view;

        harness.Service.RaiseNavigated("MainRegion", vm);

        var shown = Assert.Single(harness.Shown);
        Assert.Same(view, shown.View);
        Assert.Same(vm, shown.ViewModel);
    }

    [Fact]
    public void SameViewModel_NavigatedTwice_ReusesCachedView()
    {
        var harness = new Harness();
        harness.Attach();
        int factoryCalls = 0;
        harness.ViewFactory = _ => { factoryCalls++; return new object(); };
        var vm = new StubViewModel();

        harness.Service.RaiseNavigated("MainRegion", vm);
        harness.Service.RaiseNavigated("MainRegion", vm);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(2, harness.Shown.Count);
        Assert.Same(harness.Shown[0].View, harness.Shown[1].View);
    }

    [Fact]
    public void DifferentViewModels_CreateSeparateViews()
    {
        var harness = new Harness();
        harness.Attach();
        harness.ViewFactory = _ => new object();

        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());
        harness.Service.RaiseNavigated("MainRegion", new OtherViewModel());

        Assert.Equal(2, harness.Shown.Count);
        Assert.NotSame(harness.Shown[0].View, harness.Shown[1].View);
    }

    [Fact]
    public void RegionRename_TakesEffectImmediately()
    {
        var harness = new Harness();
        harness.Attach();
        harness.RegionName = "SidePanel";

        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());
        Assert.Empty(harness.Shown);

        harness.Service.RaiseNavigated("SidePanel", new StubViewModel());
        Assert.Single(harness.Shown);
    }

    [Fact]
    public void NoDispatcherAccess_MarshalsToDispatcher()
    {
        var dispatcher = new FakeDispatcher(hasAccess: false);
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { });
        var service = new FakeNavigationService();
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", new StubViewModel());

        // Not executed inline: queued on the dispatcher.
        Assert.Empty(shown);
        Assert.Equal(1, dispatcher.PostedCount);

        dispatcher.RunPosted();

        Assert.Single(shown);
    }

    [Fact]
    public void ViewFactoryThrows_KeepsPreviousContentAndReportsFailure()
    {
        var harness = new Harness();
        harness.Attach();
        var goodVm = new StubViewModel();
        var goodView = new object();
        var badVm = new OtherViewModel();
        var factoryError = new InvalidOperationException("boom");
        harness.ViewFactory = vm => ReferenceEquals(vm, badVm) ? throw factoryError : goodView;

        harness.Service.RaiseNavigated("MainRegion", goodVm);
        harness.Service.RaiseNavigated("MainRegion", badVm);

        // Previous content is kept...
        var shown = Assert.Single(harness.Shown);
        Assert.Same(goodView, shown.View);
        Assert.Same(goodVm, shown.ViewModel);

        // ...and the failure is reported through the hook.
        var failure = Assert.Single(harness.Failures);
        Assert.Same(badVm, failure.ViewModel);
        Assert.Same(factoryError, failure.Error);
    }

    [Fact]
    public void ViewFactoryReturnsNull_ReportedAsFailure_AndRetriedNextTime()
    {
        var harness = new Harness();
        harness.Attach();
        int calls = 0;
        harness.ViewFactory = _ => { calls++; return null!; };

        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());
        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());

        Assert.Empty(harness.Shown);
        Assert.Equal(2, harness.Failures.Count);
        Assert.All(harness.Failures, f => Assert.IsType<InvalidOperationException>(f.Error));
        Assert.Equal(2, calls); // Null results are not cached: creation is retried.
    }

    [Fact]
    public void FailedView_IsNotCached_RetriedNextTime()
    {
        var harness = new Harness();
        harness.Attach();
        int calls = 0;
        var vm = new StubViewModel();
        harness.ViewFactory = _ =>
        {
            calls++;
            return calls == 1 ? throw new InvalidOperationException("transient") : new object();
        };

        harness.Service.RaiseNavigated("MainRegion", vm);
        Assert.Empty(harness.Shown);

        harness.Service.RaiseNavigated("MainRegion", vm);

        Assert.Single(harness.Shown);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Attach_NullService_ThrowsArgumentNullException()
    {
        var harness = new Harness();

        Assert.Throws<ArgumentNullException>(() => harness.Controller.Attach(null!));
    }

    [Fact]
    public void Detach_StopsHandlingEvents()
    {
        var harness = new Harness();
        harness.Attach();
        harness.Controller.Detach();

        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());

        Assert.Empty(harness.Shown);
    }

    [Fact]
    public void Attach_Twice_ReplacesPreviousSubscription()
    {
        var harness = new Harness();
        var first = new FakeNavigationService();
        var second = new FakeNavigationService();
        harness.Controller.Attach(first);
        harness.Controller.Attach(second);

        first.RaiseNavigated("MainRegion", new StubViewModel());
        Assert.Empty(harness.Shown);

        second.RaiseNavigated("MainRegion", new StubViewModel());
        Assert.Single(harness.Shown);
    }

    [Fact]
    public void Detach_IsIdempotent()
    {
        var harness = new Harness();

        harness.Controller.Detach();
        harness.Controller.Detach(); // Must not throw.
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var dispatcher = new FakeDispatcher();

        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(null!, () => "r", _ => new object(), (_, _) => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(dispatcher, null!, _ => new object(), (_, _) => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(dispatcher, () => "r", null!, (_, _) => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(dispatcher, () => "r", _ => new object(), null!, () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(dispatcher, () => "r", _ => new object(), (_, _) => { }, null!));
    }

    [Fact]
    public void Refresh_AfterRename_ShowsNewRegionsCurrentView()
    {
        var harness = new Harness();
        harness.Attach();
        var oldVm = new StubViewModel();
        harness.Service.RaiseNavigated("MainRegion", oldVm);
        Assert.Single(harness.Shown);

        var newVm = new OtherViewModel();
        harness.Service.CurrentByRegion["OtherRegion"] = newVm;
        harness.RegionName = "OtherRegion"; // Simulates the bound RegionName changing.
        harness.Controller.Refresh();

        var shown = Assert.Single(harness.Shown.Skip(1));
        Assert.Same(newVm, shown.ViewModel);
    }

    [Fact]
    public void Refresh_EmptyRegion_ClearsContent()
    {
        var harness = new Harness();
        harness.Attach();
        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());
        Assert.Single(harness.Shown);

        harness.RegionName = "EmptyRegion";
        harness.Controller.Refresh();

        Assert.Equal(1, harness.ClearedCount);
        Assert.Single(harness.Shown); // No new view shown.
    }

    [Fact]
    public void Refresh_ReusesCachedView()
    {
        var harness = new Harness();
        harness.Attach();
        var vm = new StubViewModel();
        harness.Service.CurrentByRegion["MainRegion"] = vm;
        int factoryCalls = 0;
        harness.ViewFactory = _ => { factoryCalls++; return new object(); };

        harness.Controller.Refresh();
        harness.Controller.Refresh();

        Assert.Equal(1, factoryCalls);
        Assert.Equal(2, harness.Shown.Count);
    }

    [Fact]
    public void Refresh_WhenDetached_DoesNothing()
    {
        var harness = new Harness();
        harness.Service.CurrentByRegion["MainRegion"] = new StubViewModel();

        harness.Controller.Refresh(); // Never attached.

        Assert.Empty(harness.Shown);
        Assert.Equal(0, harness.ClearedCount);
    }

    [Fact]
    public void Refresh_BlankRegionName_ClearsInsteadOfThrowing()
    {
        var harness = new Harness();
        harness.Attach();
        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());
        Assert.Single(harness.Shown);

        harness.RegionName = "  "; // Transient empty binding value.
        harness.Controller.Refresh(); // Must not throw.

        Assert.Equal(1, harness.ClearedCount);
    }

    [Fact]
    public void QueuedCallback_WithStaleViewModel_SkippedAtExecutionTime()
    {
        // The staleness check at event time passes, but by the time the queued
        // dispatcher callback runs, navigation has moved to another ViewModel.
        // The execution-time re-check must skip the stale callback.
        var dispatcher = new FakeDispatcher(hasAccess: false);
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { },
            onViewCreationFailed: (_, _) => { });
        controller.Attach(service);

        var vmA = new StubViewModel();
        var vmB = new StubViewModel();
        service.CurrentByRegion["MainRegion"] = vmA;
        service.RaiseNavigated("MainRegion", vmA); // passes check, queued

        // Navigation moves on before the queue runs.
        service.CurrentByRegion["MainRegion"] = vmB;

        dispatcher.RunPosted();

        Assert.Empty(shown); // stale VM-A must not be applied
    }

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

    [Fact]
    public void QueuedCallback_WhenDefinitiveCurrentBecomesNull_SkippedAtExecutionTime()
    {
        // Same queueing shape as the stale-ViewModel test, but the region
        // ends up with NO current page while the event is queued — the
        // definitive null (for the built-in service: the stack emptied,
        // or the service was disposed and its pages released). Applying
        // the event would resurrect the released page's view.
        var dispatcher = new FakeDispatcher(hasAccess: false);
        var service = new DefinitiveFakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { },
            onViewCreationFailed: (_, _) => { });
        controller.Attach(service);

        var vmA = new StubViewModel();
        service.CurrentByRegion["MainRegion"] = vmA;
        service.RaiseNavigated("MainRegion", vmA); // passes checks, queued

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
        var dispatcher = new FakeDispatcher(hasAccess: false);
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { },
            onViewCreationFailed: (_, _) => { });
        controller.Attach(service);

        var vmA = new StubViewModel();
        service.CurrentByRegion["MainRegion"] = vmA;
        service.RaiseNavigated("MainRegion", vmA); // passes checks, queued

        service.CurrentByRegion.Remove("MainRegion");

        dispatcher.RunPosted();

        var shownEntry = Assert.Single(shown);
        Assert.Same(vmA, shownEntry.ViewModel);
    }

    [Fact]
    public void Factory_ReceivesRegionAndParameterFromService()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var service = new FakeNavigationService();
        var vm = new StubViewModel();
        service.CurrentByRegion["MainRegion"] = vm;
        service.ParametersByViewModel[vm] = "param-1";
        ViewCreationContext? received = null;
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: ctx => { received = ctx; return new object(); },
            showView: (_, _) => { },
            clearView: () => { });
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", vm);

        Assert.NotNull(received);
        Assert.Same(vm, received!.ViewModel);
        Assert.Equal("MainRegion", received.RegionName);
        Assert.Equal("param-1", received.Parameter);
    }

    [Fact]
    public void Factory_RunsOncePerViewModel_ParameterChangeDoesNotRecreateView()
    {
        // Views are cached per ViewModel instance: a later activation of the
        // same instance (even with a different parameter) reuses the view,
        // so the factory — and its context — reflect the first activation.
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var service = new FakeNavigationService();
        var vm = new StubViewModel();
        service.CurrentByRegion["MainRegion"] = vm;
        service.ParametersByViewModel[vm] = "param-1";
        var contexts = new List<ViewCreationContext>();
        var shown = new List<object>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: ctx => { contexts.Add(ctx); return new object(); },
            showView: (view, _) => shown.Add(view),
            clearView: () => { });
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", vm);
        service.ParametersByViewModel[vm] = "param-2";
        service.RaiseNavigated("MainRegion", vm);

        var context = Assert.Single(contexts);
        Assert.Equal("param-1", context.Parameter);
        Assert.Equal(2, shown.Count);
        Assert.Same(shown[0], shown[1]);
    }
}
