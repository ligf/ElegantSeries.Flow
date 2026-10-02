using System.Runtime.ExceptionServices;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;
using ElegantSeries.Flow.Avalonia.Hosting;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Avalonia.Threading;

namespace ElegantSeries.Flow.Avalonia.Tests;

public sealed class NavigationHostTests
{
    private static NavigationHost CreateHost(
        FakeDispatcher dispatcher,
        FakeNavigationService service,
        IViewLocator locator,
        string regionName = "MainRegion")
    {
        return new NavigationHost(dispatcher)
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
        using var host = new NavigationHost(new FakeDispatcher());

        Assert.Equal("MainRegion", host.RegionName);
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
