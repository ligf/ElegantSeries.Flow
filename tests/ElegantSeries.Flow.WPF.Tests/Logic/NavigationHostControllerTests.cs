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
    public void FailureHookThrows_OnSynchronousPath_SwallowedAndContentKept()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        int hookCalls = 0;
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => throw new InvalidOperationException("factory boom"),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { },
            onViewCreationFailed: (_, _) => { hookCalls++; throw new InvalidOperationException("hook boom"); });
        var service = new FakeNavigationService();
        controller.Attach(service);

        // The hook is the terminal channel: its exception must not escape
        // into the event raiser (where the real service would isolate it as
        // a subscriber error, disguising a view failure as a hook failure).
        var escaped = Record.Exception(() => service.RaiseNavigated("MainRegion", new StubViewModel()));

        Assert.Null(escaped);
        Assert.Equal(1, hookCalls);
        Assert.Empty(shown);
    }

    [Fact]
    public void FailureHookThrows_OnQueuedPath_SwallowedInPostedCallback()
    {
        var dispatcher = new FakeDispatcher(hasAccess: false);
        int hookCalls = 0;
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => throw new InvalidOperationException("factory boom"),
            showView: (_, _) => { },
            clearView: () => { },
            onViewCreationFailed: (_, _) => { hookCalls++; throw new InvalidOperationException("hook boom"); });
        var service = new FakeNavigationService();
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", new StubViewModel());
        Assert.Equal(1, dispatcher.PostedCount);

        // The hook's exception must not escape the posted callback (where
        // it would become an unhandled dispatcher exception).
        var escaped = Record.Exception(() => dispatcher.RunPosted());

        Assert.Null(escaped);
        Assert.Equal(1, hookCalls);
    }

    [Fact]
    public void FailureHookThrows_OnRefreshPath_Swallowed()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        int hookCalls = 0;
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => throw new InvalidOperationException("factory boom"),
            showView: (_, _) => { },
            clearView: () => { },
            onViewCreationFailed: (_, _) => { hookCalls++; throw new InvalidOperationException("hook boom"); });
        var service = new FakeNavigationService();
        service.CurrentByRegion["MainRegion"] = new StubViewModel();
        controller.Attach(service);

        // Refresh is invoked from property setters on the platform hosts:
        // the hook's exception must not leak to the setter's caller.
        var escaped = Record.Exception(() => controller.Refresh());

        Assert.Null(escaped);
        Assert.Equal(1, hookCalls);
    }

    [Fact]
    public void ShowViewThrows_OnSynchronousPath_ReportsThroughHookAndKeepsPreviousContent()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var failures = new List<(INavigationViewModel ViewModel, Exception Error)>();
        var goodVm = new StubViewModel();
        var badVm = new OtherViewModel();
        var showError = new InvalidOperationException("show boom");
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) =>
            {
                if (ReferenceEquals(vm, badVm))
                {
                    throw showError;
                }

                shown.Add((view, vm));
            },
            clearView: () => { },
            onViewCreationFailed: (vm, ex) => failures.Add((vm, ex)));
        var service = new FakeNavigationService();
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", goodVm);
        var escaped = Record.Exception(() => service.RaiseNavigated("MainRegion", badVm));

        // Showing runs view code (a view's DataContext initialization):
        // its failure takes the creation failure's path — no escape into
        // the event raiser, previous content kept, hook reports.
        Assert.Null(escaped);
        var single = Assert.Single(shown);
        Assert.Same(goodVm, single.ViewModel);
        var failure = Assert.Single(failures);
        Assert.Same(badVm, failure.ViewModel);
        Assert.Same(showError, failure.Error);
    }

    [Fact]
    public void ShowViewThrows_OnQueuedPath_ReportsThroughHookAndDoesNotEscapePostedCallback()
    {
        var dispatcher = new FakeDispatcher(hasAccess: false);
        var failures = new List<(INavigationViewModel ViewModel, Exception Error)>();
        var showError = new InvalidOperationException("show boom");
        var vm = new StubViewModel();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (_, _) => throw showError,
            clearView: () => { },
            onViewCreationFailed: (failedVm, ex) => failures.Add((failedVm, ex)));
        var service = new FakeNavigationService();
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", vm);
        Assert.Equal(1, dispatcher.PostedCount);

        // An escaping show failure would become an unhandled dispatcher
        // exception here.
        var escaped = Record.Exception(() => dispatcher.RunPosted());

        Assert.Null(escaped);
        var failure = Assert.Single(failures);
        Assert.Same(vm, failure.ViewModel);
        Assert.Same(showError, failure.Error);
    }

    [Fact]
    public void ShowViewThrows_OnRefreshPath_ReportsThroughHookAndDoesNotEscape()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var failures = new List<(INavigationViewModel ViewModel, Exception Error)>();
        var showError = new InvalidOperationException("show boom");
        var vm = new StubViewModel();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (_, _) => throw showError,
            clearView: () => { },
            onViewCreationFailed: (failedVm, ex) => failures.Add((failedVm, ex)));
        var service = new FakeNavigationService();
        service.CurrentByRegion["MainRegion"] = vm;
        controller.Attach(service);

        // Refresh runs under the platform hosts' property setters: the
        // show failure must not leak to the setter's caller.
        var escaped = Record.Exception(() => controller.Refresh());

        Assert.Null(escaped);
        var failure = Assert.Single(failures);
        Assert.Same(vm, failure.ViewModel);
        Assert.Same(showError, failure.Error);
    }

    [Fact]
    public void ShowViewThrows_FailureHookAlsoThrows_Swallowed()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        int hookCalls = 0;
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (_, _) => throw new InvalidOperationException("show boom"),
            clearView: () => { },
            onViewCreationFailed: (_, _) => { hookCalls++; throw new InvalidOperationException("hook boom"); });
        var service = new FakeNavigationService();
        controller.Attach(service);

        var escaped = Record.Exception(() => service.RaiseNavigated("MainRegion", new StubViewModel()));

        Assert.Null(escaped);
        Assert.Equal(1, hookCalls);
    }

    [Fact]
    public void ClearViewThrows_OnRefresh_ReportsAgainstLastShownPageAndDoesNotEscape()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var failures = new List<(INavigationViewModel ViewModel, Exception Error)>();
        var clearError = new InvalidOperationException("clear boom");
        var vm = new StubViewModel();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, shownVm) => shown.Add((view, shownVm)),
            clearView: () => throw clearError,
            onViewCreationFailed: (failedVm, ex) => failures.Add((failedVm, ex)));
        var service = new FakeNavigationService();
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", vm);
        Assert.Single(shown);

        // The region is empty, so Refresh clears the host; the clear
        // runs the outgoing view's code and throws. The failure is
        // reported against the page still on screen and never escapes.
        var escaped = Record.Exception(() => controller.Refresh());

        Assert.Null(escaped);
        var failure = Assert.Single(failures);
        Assert.Same(vm, failure.ViewModel);
        Assert.Same(clearError, failure.Error);
    }

    [Fact]
    public void ClearViewThrows_AfterFailedShow_ReportsAgainstPageStillOnScreen()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var failures = new List<(INavigationViewModel ViewModel, Exception Error)>();
        var showError = new InvalidOperationException("show boom");
        var clearError = new InvalidOperationException("clear boom");
        var goodVm = new StubViewModel();
        var badVm = new OtherViewModel();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) =>
            {
                if (ReferenceEquals(vm, badVm))
                {
                    throw showError;
                }

                shown.Add((view, vm));
            },
            clearView: () => throw clearError,
            onViewCreationFailed: (vm, ex) => failures.Add((vm, ex)));
        var service = new FakeNavigationService();
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", goodVm);
        service.RaiseNavigated("MainRegion", badVm);

        // The failed show never replaced the shown page, so the clear
        // failure that follows is attributed to the page still on
        // screen — not to the page that failed to show.
        var escaped = Record.Exception(() => controller.Refresh());

        Assert.Null(escaped);
        Assert.Single(shown);
        Assert.Equal(2, failures.Count);
        Assert.Same(badVm, failures[0].ViewModel);
        Assert.Same(showError, failures[0].Error);
        Assert.Same(goodVm, failures[1].ViewModel);
        Assert.Same(clearError, failures[1].Error);
    }

    [Fact]
    public void ClearViewThrows_WithNothingShown_SwallowedWithoutReport()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var failures = new List<(INavigationViewModel ViewModel, Exception Error)>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (_, _) => { },
            clearView: () => throw new InvalidOperationException("clear boom"),
            onViewCreationFailed: (vm, ex) => failures.Add((vm, ex)));
        var service = new FakeNavigationService();
        controller.Attach(service);

        // Nothing was ever shown, so there is no page to attribute the
        // clear failure to: it is swallowed rather than escaping
        // Refresh, and the hook is not invoked.
        var escaped = Record.Exception(() => controller.Refresh());

        Assert.Null(escaped);
        Assert.Empty(failures);
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
    public void Detach_ThenAttach_Resubscribes()
    {
        // Detach is the reversible unsubscribe (visual-tree detach, service
        // switch): attaching again afterwards must work exactly as before.
        var harness = new Harness();
        harness.Attach();
        harness.Controller.Detach();
        harness.Attach();

        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());

        Assert.Single(harness.Shown);
    }

    [Fact]
    public void Close_UnsubscribesAndBlocksLaterAttach()
    {
        // Close models host disposal racing an in-flight property change:
        // the change passed the host's disposed check just before Dispose
        // ran and calls Attach afterwards. The disposed host must stay
        // unsubscribed, or the (usually singleton) service keeps it alive
        // and keeps delivering events to it.
        var harness = new Harness();
        harness.Attach();
        harness.Controller.Close();

        harness.Attach(); // The racing property change lands after disposal.

        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());

        Assert.Empty(harness.Shown);
    }

    [Fact]
    public void Close_IsIdempotent()
    {
        var harness = new Harness();
        harness.Attach();

        harness.Controller.Close();
        harness.Controller.Close(); // Must not throw.

        harness.Service.RaiseNavigated("MainRegion", new StubViewModel());
        Assert.Empty(harness.Shown);
    }

    [Fact]
    public async Task Refresh_RacingClose_ViewNotShown()
    {
        // A refresh (or property change) already in flight when the host is
        // disposed from another thread has captured the service and is busy
        // creating the view. When creation finishes, the closed check must
        // drop the view instead of showing it on the disposed host.
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ =>
            {
                factoryEntered.TrySetResult();
                factoryRelease.Task.GetAwaiter().GetResult();
                return new object();
            },
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { });
        controller.Attach(service);
        service.CurrentByRegion["MainRegion"] = new StubViewModel();

        var refreshTask = Task.Run(() => controller.Refresh(), TestContext.Current.CancellationToken);
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        controller.Close();
        factoryRelease.TrySetResult();
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Empty(shown);
    }

    [Fact]
    public async Task EventCapturedBeforeClose_FactoryNotInvoked()
    {
        // The event handler is already running (blocked in the region-name
        // provider, its first step) when the host is disposed from another
        // thread. When the handler resumes, it must not reach the view
        // factory: no user code runs on a disposed host.
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var service = new FakeNavigationService();
        int factoryCalls = 0;
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int providerCalls = 0;
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () =>
            {
                if (Interlocked.Increment(ref providerCalls) == 1)
                {
                    providerEntered.TrySetResult();
                    providerRelease.Task.GetAwaiter().GetResult();
                }
                return "MainRegion";
            },
            viewFactory: _ => { factoryCalls++; return new object(); },
            showView: (_, _) => { },
            clearView: () => { });
        controller.Attach(service);

        var raiseTask = Task.Run(
            () => service.RaiseNavigated("MainRegion", new StubViewModel()),
            TestContext.Current.CancellationToken);
        await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        controller.Close();
        providerRelease.TrySetResult();
        await raiseTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task FactoryThrowsAfterClose_FailureHookNotInvoked()
    {
        // View creation is in flight when the host is disposed, and the
        // factory then throws. The failure hook is user code: after closure
        // it must not run on the disposed host.
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var service = new FakeNavigationService();
        int hookCalls = 0;
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ =>
            {
                factoryEntered.TrySetResult();
                factoryRelease.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("factory boom");
            },
            showView: (_, _) => { },
            clearView: () => { },
            onViewCreationFailed: (_, _) => hookCalls++);
        controller.Attach(service);
        service.CurrentByRegion["MainRegion"] = new StubViewModel();

        var refreshTask = Task.Run(() => controller.Refresh(), TestContext.Current.CancellationToken);
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        controller.Close();
        factoryRelease.TrySetResult();
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, hookCalls);
    }

    [Fact]
    public void QueuedCallback_AfterClose_NotApplied()
    {
        // An event already posted to the dispatcher when the host is
        // disposed must not apply its (stale) page afterwards: Close drops
        // the current service, so the execution-time re-check sees the
        // callback as superseded.
        var dispatcher = new FakeDispatcher(hasAccess: false);
        var service = new FakeNavigationService();
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { });
        controller.Attach(service);

        var vm = new StubViewModel();
        service.CurrentByRegion["MainRegion"] = vm;
        service.RaiseNavigated("MainRegion", vm); // passes checks, queued
        Assert.Equal(1, dispatcher.PostedCount);

        controller.Close();
        dispatcher.RunPosted();

        Assert.Empty(shown);
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
    public void SyncCallback_WhenDefinitiveCurrentIsNull_SkippedAtReceiptTime()
    {
        // The dispatcher has access, so the event is applied synchronously —
        // no queue, no execution-time re-check. The handler observes a
        // definitive null: an earlier subscriber of the same event disposed
        // the service mid-dispatch, emptying the region before this handler
        // ran. Applying the event would resurrect the released page's view,
        // so the receipt-time check must judge it stale, exactly as the
        // queued path's execution-time re-check does.
        var dispatcher = new FakeDispatcher(hasAccess: true);
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
        // CurrentByRegion has no entry for MainRegion: the handler observes
        // the region after it lost its page.
        service.RaiseNavigated("MainRegion", vmA);

        Assert.Empty(shown); // the released page must not be resurrected
    }

    [Fact]
    public void SyncCallback_WhenDefinitiveCurrentIsEventViewModel_AppliedAtReceiptTime()
    {
        // Happy path for a definitive service on the synchronous path: the
        // current page IS the event's ViewModel, so the event is applied —
        // the definitive check must only drop events that no longer name
        // the current page, never live ones.
        var dispatcher = new FakeDispatcher(hasAccess: true);
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
        service.RaiseNavigated("MainRegion", vmA);

        var shownEntry = Assert.Single(shown);
        Assert.Same(vmA, shownEntry.ViewModel);
    }

    [Fact]
    public void SyncCallback_WhenUnknownCurrentIsNull_StillAppliedAtReceiptTime()
    {
        // Compatibility rule on the synchronous path: for implementations
        // without the definitive guarantee, a null current means "unknown",
        // so the event is applied exactly as before.
        var dispatcher = new FakeDispatcher(hasAccess: true);
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
        service.RaiseNavigated("MainRegion", vmA);

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

    [Fact]
    public void SnapshotDeliveryAfterDetach_AppliesNothing()
    {
        // An event dispatch iterates a snapshot of its invocation list:
        // when an earlier subscriber detaches the host mid-dispatch, the
        // controller's handler still runs — against a detached host whose
        // service reference is already null. It must apply nothing.
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        int factoryCalls = 0;
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => { factoryCalls++; return new object(); },
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { });
        var service = new FakeNavigationService();
        using var detacher = service.SubscribeRegionNavigated((_, _) => controller.Detach());
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", new StubViewModel());

        Assert.Equal(0, factoryCalls);
        Assert.Empty(shown);
    }

    [Fact]
    public void SnapshotDeliveryAfterDetach_QueuesNothing()
    {
        // The queued path has the same hole without the receipt-time
        // guard: the captured service and the execution-time service are
        // both null after a mid-dispatch detach, so the detachment
        // re-check passes vacuously. Nothing may even be queued.
        var dispatcher = new FakeDispatcher(hasAccess: false);
        var shown = new List<(object View, INavigationViewModel ViewModel)>();
        var controller = new NavigationHostController<object>(
            dispatcher,
            regionNameProvider: () => "MainRegion",
            viewFactory: _ => new object(),
            showView: (view, vm) => shown.Add((view, vm)),
            clearView: () => { });
        var service = new FakeNavigationService();
        using var detacher = service.SubscribeRegionNavigated((_, _) => controller.Detach());
        controller.Attach(service);

        service.RaiseNavigated("MainRegion", new StubViewModel());

        Assert.Equal(0, dispatcher.PostedCount);
        dispatcher.RunPosted();
        Assert.Empty(shown);
    }
}
