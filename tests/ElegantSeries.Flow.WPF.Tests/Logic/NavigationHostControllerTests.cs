using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;
using ElegantSeries.Flow.Core.Hosting;

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
        public NavigationHostController<object> Controller { get; }

        public Harness()
        {
            Controller = new NavigationHostController<object>(
                Dispatcher,
                regionNameProvider: () => RegionName,
                viewFactory: vm => ViewFactory(vm),
                showView: (view, vm) => Shown.Add((view, vm)),
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
            showView: (view, vm) => shown.Add((view, vm)));
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
            new NavigationHostController<object>(null!, () => "r", _ => new object(), (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(dispatcher, null!, _ => new object(), (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(dispatcher, () => "r", null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationHostController<object>(dispatcher, () => "r", _ => new object(), null!));
    }
}
