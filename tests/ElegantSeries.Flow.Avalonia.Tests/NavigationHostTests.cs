using System.Runtime.ExceptionServices;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;
using ElegantSeries.Flow.Avalonia.Hosting;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Avalonia.Threading;

namespace ElegantSeries.Flow.Avalonia.Tests;

public sealed class NavigationHostTests
{
    private sealed class TestNavigationHost : NavigationHost
    {
        public TestNavigationHost(IDispatcher dispatcher)
            : base(dispatcher)
        {
        }
    }

    private sealed class CoreOnlyDispatcher : ElegantSeries.Flow.Threading.IDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }

    private sealed class CoreDispatcherHost : NavigationHost
    {
        public CoreDispatcherHost(ElegantSeries.Flow.Threading.IDispatcher dispatcher)
            : base(dispatcher)
        {
        }
    }

    private static NavigationHost CreateHost(
        FakeDispatcher dispatcher,
        FakeNavigationService service,
        IViewLocator locator,
        string regionName = "MainRegion")
    {
        return new TestNavigationHost(dispatcher)
        {
            RegionName = regionName,
            NavigationService = service,
            ViewLocator = locator,
        };
    }

    private static ViewLocator CreateLocator()
    {
        var locator = new ViewLocator();
        locator.Register<TestView, TestViewModel>();
        return locator;
    }

    [Fact]
    public void RegionName_DefaultsToMainRegion()
    {
        using var host = new TestNavigationHost(new FakeDispatcher());

        Assert.Equal("MainRegion", host.RegionName);
    }

    [Fact]
    public void ProtectedConstructor_AcceptsCoreOnlyDispatcher()
    {
        // Pins the constructor's parameter type to the Core IDispatcher (as in
        // the WPF host): a dispatcher implementing only the Core interface —
        // not the Avalonia one — must be accepted. This test does not compile
        // against the previous Avalonia-IDispatcher signature.
        using var host = new CoreDispatcherHost(new CoreOnlyDispatcher());

        Assert.Equal("MainRegion", host.RegionName);
    }

    [Fact]
    public void RegionName_Padded_MatchesTheTrimmedServiceSpelling()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        using var host = CreateHost(dispatcher, service, CreateLocator(), regionName: "  MainRegion  ");
        var vm = new TestViewModel();

        // The getter reports the normalized spelling, and the host reacts
        // to the service's (normalized) region events: without the mirror
        // trim the ordinal filter would drop every event.
        Assert.Equal("MainRegion", host.RegionName);
        service.RaiseRegionNavigated("MainRegion", vm);

        var view = Assert.IsType<TestView>(host.Content);
        Assert.Same(vm, view.DataContext);
    }

    [Fact]
    public void RegionNavigated_MatchingRegion_SetsContentWithDataContext()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        using var host = CreateHost(dispatcher, service, CreateLocator());
        var vm = new TestViewModel();

        service.RaiseRegionNavigated("MainRegion", vm);

        var view = Assert.IsType<TestView>(host.Content);
        Assert.Same(vm, view.DataContext);
    }

    [Fact]
    public void RegionNavigated_OtherRegion_Ignored()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        // A locator that throws if CreateView is ever called proves the event was ignored.
        var locator = new ViewLocator();
        using var host = CreateHost(dispatcher, service, locator);
        var vm = new TestViewModel();

        service.RaiseRegionNavigated("OtherRegion", vm);

        Assert.Null(host.Content);
    }

    [Fact]
    public void RegionNavigated_RaisedOnBackgroundThread_DoesNotTouchPropertySystem()
    {
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new FakeNavigationService();
        using var host = CreateHost(dispatcher, service, CreateLocator());
        var vm = new TestViewModel();

        // The host was constructed on the test thread, so its Avalonia dispatcher
        // belongs to that thread: reading the RegionName StyledProperty on another
        // thread throws InvalidOperationException ("...different thread owns it").
        // The handler must compare against a mirrored field instead and then marshal
        // to the UI thread. (FakeNavigationService does not swallow subscriber
        // exceptions, unlike the real service, so a regression fails loudly here
        // instead of silently.)
        RaiseOnBackgroundThread(() => service.RaiseRegionNavigated("MainRegion", vm));

        Assert.Single(dispatcher.PostedActions);
        dispatcher.RunPosted();

        var view = Assert.IsType<TestView>(host.Content);
        Assert.Same(vm, view.DataContext);
    }

    [Fact]
    public void RegionName_RenamedAfterConstruction_BackgroundEventUsesNewName()
    {
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new FakeNavigationService();
        using var host = CreateHost(dispatcher, service, CreateLocator());

        // Renamed on the (fake) UI thread; the background handler must observe it.
        host.RegionName = "Sidebar";

        var vm = new TestViewModel();
        RaiseOnBackgroundThread(() => service.RaiseRegionNavigated("Sidebar", vm));
        Assert.Single(dispatcher.PostedActions);

        // The old name no longer matches.
        RaiseOnBackgroundThread(() => service.RaiseRegionNavigated("MainRegion", new TestViewModel()));
        Assert.Single(dispatcher.PostedActions);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a dedicated background thread and rethrows
    /// any exception on the test thread, preserving its stack trace. The test thread
    /// stays put so Avalonia thread-affinity checks keep working for assertions.
    /// </summary>
    private static void RaiseOnBackgroundThread(Action action)
    {
        Exception? backgroundError = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                backgroundError = ex;
            }
        });
        thread.Start();
        thread.Join();

        if (backgroundError is not null)
        {
            ExceptionDispatchInfo.Capture(backgroundError).Throw();
        }
    }

    [Fact]
    public void RegionNavigated_OffUiThread_MarshalsThroughDispatcher()
    {
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new FakeNavigationService();
        using var host = CreateHost(dispatcher, service, CreateLocator());
        var vm = new TestViewModel();

        service.RaiseRegionNavigated("MainRegion", vm);

        // Not applied yet: the work was posted to the UI thread.
        Assert.Null(host.Content);
        Assert.Single(dispatcher.PostedActions);

        dispatcher.RunPosted();

        var view = Assert.IsType<TestView>(host.Content);
        Assert.Same(vm, view.DataContext);
    }

    private sealed class RecordingNavigationHost : NavigationHost
    {
        public List<(INavigationViewModel ViewModel, Exception Exception)> Failures { get; } = [];

        public RecordingNavigationHost(IDispatcher dispatcher)
            : base(dispatcher)
        {
        }

        protected override void OnViewCreationFailed(INavigationViewModel viewModel, Exception exception)
        {
            Failures.Add((viewModel, exception));
        }
    }

    [Fact]
    public void ViewFactoryThrows_OnUiThread_ContentPreservedAndFailureReported()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        var locator = CreateLocator();
        locator.Register<OtherViewModel>(_ => throw new InvalidOperationException("boom"));
        using var host = new RecordingNavigationHost(dispatcher)
        {
            RegionName = "MainRegion",
            NavigationService = service,
            ViewLocator = locator,
        };
        var firstVm = new TestViewModel();
        service.RaiseRegionNavigated("MainRegion", firstVm);
        var firstView = Assert.IsType<TestView>(host.Content);

        var failedVm = new OtherViewModel();
        service.RaiseRegionNavigated("MainRegion", failedVm);

        // The host was not left in a half-updated state, and the failure is
        // observable through the hook (it cannot be thrown: the navigation
        // service swallows subscriber exceptions by design).
        Assert.Same(firstView, host.Content);
        Assert.Same(firstVm, firstView.DataContext);
        var failure = Assert.Single(host.Failures);
        Assert.Same(failedVm, failure.ViewModel);
        Assert.Equal("boom", failure.Exception.Message);
    }

    [Fact]
    public void ViewFactoryThrows_OffUiThread_ContentPreservedAndFailureReported()
    {
        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        var service = new FakeNavigationService();
        var locator = CreateLocator();
        locator.Register<OtherViewModel>(_ => throw new InvalidOperationException("boom"));
        using var host = new RecordingNavigationHost(dispatcher)
        {
            RegionName = "MainRegion",
            NavigationService = service,
            ViewLocator = locator,
        };
        var firstVm = new TestViewModel();

        // First navigation succeeds; run it on the (fake) UI thread.
        dispatcher.IsOnUiThread = true;
        service.RaiseRegionNavigated("MainRegion", firstVm);
        var firstView = Assert.IsType<TestView>(host.Content);

        // Second navigation arrives off the UI thread; the posted callback must not
        // tear down the message loop, the previous content must stay intact, and
        // the failure must still be reported through the hook.
        dispatcher.IsOnUiThread = false;
        var failedVm = new OtherViewModel();
        service.RaiseRegionNavigated("MainRegion", failedVm);
        dispatcher.RunPosted();

        Assert.Same(firstView, host.Content);
        Assert.Same(firstVm, firstView.DataContext);
        var failure = Assert.Single(host.Failures);
        Assert.Same(failedVm, failure.ViewModel);
        Assert.Equal("boom", failure.Exception.Message);
    }

    [Fact]
    public void ViewLocatorNotSet_ReportsFailureThroughHook()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        using var host = new RecordingNavigationHost(dispatcher)
        {
            RegionName = "MainRegion",
            NavigationService = service,
        };

        var vm = new TestViewModel();
        service.RaiseRegionNavigated("MainRegion", vm);

        var failure = Assert.Single(host.Failures);
        Assert.Same(vm, failure.ViewModel);
        Assert.Contains(nameof(IViewLocator), failure.Exception.Message);
        Assert.Null(host.Content);
    }

    private sealed class AttachAwareNavigationHost : NavigationHost
    {
        public AttachAwareNavigationHost(IDispatcher dispatcher)
            : base(dispatcher)
        {
        }

        // Drives the host's attach/detach bookkeeping directly: the real
        // overrides require a visual tree, which these tests do not have.
        public void SimulateAttachedToVisualTree() => HandleAttachedToVisualTree();

        public void SimulateDetachedFromVisualTree() => HandleDetachedFromVisualTree();
    }

    [Fact]
    public void Detached_ServiceChange_DeferredUntilReattach()
    {
        var dispatcher = new FakeDispatcher();
        var firstService = new FakeNavigationService();
        var secondService = new FakeNavigationService();
        using var host = new AttachAwareNavigationHost(dispatcher)
        {
            RegionName = "MainRegion",
            NavigationService = firstService,
            ViewLocator = CreateLocator(),
        };
        host.SimulateAttachedToVisualTree();
        var firstVm = new TestViewModel();
        firstService.RaiseRegionNavigated("MainRegion", firstVm);
        var firstView = Assert.IsType<TestView>(host.Content);

        host.SimulateDetachedFromVisualTree();
        host.NavigationService = secondService;

        // While detached, the change must not resubscribe the host: the
        // detach dropped the subscription precisely so the service cannot
        // keep the host alive or drive view creation on an invisible host.
        secondService.RaiseRegionNavigated("MainRegion", new TestViewModel());
        Assert.Same(firstView, host.Content);

        // Reattaching applies the deferred change: the new service now
        // drives the host, the old one no longer does.
        host.SimulateAttachedToVisualTree();
        var secondVm = new TestViewModel();
        secondService.RaiseRegionNavigated("MainRegion", secondVm);
        var secondView = Assert.IsType<TestView>(host.Content);
        Assert.Same(secondVm, secondView.DataContext);

        firstService.RaiseRegionNavigated("MainRegion", new TestViewModel());
        Assert.Same(secondView, host.Content);
    }

    [Fact]
    public void Detached_SameServiceReassigned_DoesNotResubscribeUntilReattach()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        using var host = new AttachAwareNavigationHost(dispatcher)
        {
            RegionName = "MainRegion",
            NavigationService = service,
            ViewLocator = CreateLocator(),
        };
        host.SimulateAttachedToVisualTree();
        var firstVm = new TestViewModel();
        service.RaiseRegionNavigated("MainRegion", firstVm);
        var firstView = Assert.IsType<TestView>(host.Content);

        host.SimulateDetachedFromVisualTree();

        // Reassigning the very same service while detached must not
        // resurrect the subscription the detach dropped (the detach nulled
        // the tracked subscription, so without the gate this looks like a
        // service change).
        host.NavigationService = null;
        host.NavigationService = service;
        service.RaiseRegionNavigated("MainRegion", new TestViewModel());
        Assert.Same(firstView, host.Content);

        // After reattach, the same service drives the host again.
        host.SimulateAttachedToVisualTree();
        var secondVm = new TestViewModel();
        service.RaiseRegionNavigated("MainRegion", secondVm);
        var secondView = Assert.IsType<TestView>(host.Content);
        Assert.Same(secondVm, secondView.DataContext);
    }

    [Fact]
    public void Dispose_UnsubscribesFromService()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        var host = CreateHost(dispatcher, service, CreateLocator());
        var firstVm = new TestViewModel();
        service.RaiseRegionNavigated("MainRegion", firstVm);
        var firstView = Assert.IsType<TestView>(host.Content);

        host.Dispose();
        service.RaiseRegionNavigated("MainRegion", new TestViewModel());

        Assert.Same(firstView, host.Content);
    }

    [Fact]
    public void Dispose_PropertySettersThrowAfterDispose()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        var host = CreateHost(dispatcher, service, CreateLocator());

        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => host.RegionName = "SidePanel");
        Assert.Throws<ObjectDisposedException>(() => host.NavigationService = new FakeNavigationService());
        Assert.Throws<ObjectDisposedException>(() => host.ViewLocator = CreateLocator());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        var host = CreateHost(dispatcher, service, CreateLocator());

        host.Dispose();
        host.Dispose(); // Must not throw.
    }

    [Fact]
    public void Dispose_FromBackgroundThread_DoesNotThrow()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        var host = CreateHost(dispatcher, service, CreateLocator());

        // Dispose only touches plain fields, never the property system, so
        // any thread can call it.
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                host.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void SetValueDirectlyAfterDispose_DoesNotResurrectSubscription()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        var host = CreateHost(dispatcher, service, CreateLocator());
        host.Dispose();
        var second = new FakeNavigationService();

        // A stale binding writes through the property system, bypassing the
        // CLR setter: the subscription update must ignore it.
        host.SetValue(NavigationHost.NavigationServiceProperty, second);
        second.RaiseRegionNavigated("MainRegion", new TestViewModel());

        Assert.Null(host.Content);
    }

    [Fact]
    public void ChangingNavigationService_UnsubscribesOldAndSubscribesNew()
    {
        var dispatcher = new FakeDispatcher();
        var oldService = new FakeNavigationService();
        var newService = new FakeNavigationService();
        using var host = CreateHost(dispatcher, oldService, CreateLocator());

        host.NavigationService = newService;

        oldService.RaiseRegionNavigated("MainRegion", new TestViewModel());
        Assert.Null(host.Content);

        var vm = new TestViewModel();
        newService.RaiseRegionNavigated("MainRegion", vm);
        Assert.IsType<TestView>(host.Content);
    }

    [Fact]
    public void SameViewModel_NavigatedAgain_ReusesCachedView()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        using var host = CreateHost(dispatcher, service, CreateLocator());
        var vm = new TestViewModel();

        service.RaiseRegionNavigated("MainRegion", vm);
        var firstView = Assert.IsType<TestView>(host.Content);

        // KeepAlive scenario: the same ViewModel instance becomes active again.
        service.RaiseRegionNavigated("MainRegion", vm);
        Assert.Same(firstView, host.Content);
    }

    [Fact]
    public void DifferentViewModels_GetDifferentViews()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeNavigationService();
        using var host = CreateHost(dispatcher, service, CreateLocator());

        service.RaiseRegionNavigated("MainRegion", new TestViewModel());
        var firstView = Assert.IsType<TestView>(host.Content);

        var secondVm = new TestViewModel();
        service.RaiseRegionNavigated("MainRegion", secondVm);
        var secondView = Assert.IsType<TestView>(host.Content);

        Assert.NotSame(firstView, secondView);
        Assert.Same(secondVm, secondView.DataContext);
    }
}
