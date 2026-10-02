using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;
using ElegantSeries.Flow.WPF.Hosting;
using ElegantSeries.Flow.WPF.Locating;
using ElegantSeries.Flow.WPF.Threading;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// Windows-only tests for <see cref="NavigationHost"/> wiring: event subscription,
/// region filtering, UI-thread marshaling, view caching, failure handling, and disposal.
/// The underlying decision logic is covered by the Linux-runnable
/// <c>NavigationHostControllerTests</c>.
/// </summary>
public sealed class NavigationHostTests
{
    private sealed class TestView : FrameworkElement
    {
    }

    private sealed class TestHost : NavigationHost
    {
        public TestHost(IDispatcher dispatcher)
            : base(dispatcher)
        {
        }

        public List<(INavigationViewModel ViewModel, Exception Error)> Failures { get; } = new();

        protected override void OnViewCreationFailed(INavigationViewModel viewModel, Exception exception)
        {
            Failures.Add((viewModel, exception));
        }
    }

    private static (TestHost Host, FakeDispatcher Dispatcher, FakeNavigationService Service) CreateHost()
    {
        var dispatcher = new FakeDispatcher(hasAccess: true);
        var host = new TestHost(dispatcher);
        var service = new FakeNavigationService();
        var locator = new ViewLocator();
        locator.Register<TestView, StubViewModel>();
        host.ViewLocator = locator;
        host.NavigationService = service;
        return (host, dispatcher, service);
    }

    [Fact]
    public void PublicConstructor_UsesWpfDispatcher_WorksEndToEnd()
    {
        StaHelper.Run(() =>
        {
            using var host = new NavigationHost();
            var service = new FakeNavigationService();
            var locator = new ViewLocator();
            locator.Register<TestView, StubViewModel>();
            host.ViewLocator = locator;
            host.NavigationService = service;

            var vm = new StubViewModel();
            service.RaiseNavigated("MainRegion", vm);

            // The real WpfDispatcher reports CheckAccess == true on this STA thread,
            // so the view is applied inline.
            var view = Assert.IsType<TestView>(host.Content);
            Assert.Same(vm, view.DataContext);
        });
    }

    [Fact]
    public void RegionNavigated_MatchingRegion_ShowsViewWithDataContext()
    {
        StaHelper.Run(() =>
        {
            var (host, _, service) = CreateHost();
            var vm = new StubViewModel();

            service.RaiseNavigated("MainRegion", vm);

            var view = Assert.IsType<TestView>(host.Content);
            Assert.Same(vm, view.DataContext);
        });
    }

    [Fact]
    public void RegionNavigated_OtherRegion_Ignored()
    {
        StaHelper.Run(() =>
        {
            var (host, _, service) = CreateHost();

            service.RaiseNavigated("OtherRegion", new StubViewModel());

            Assert.Null(host.Content);
        });
    }

    [Fact]
    public void RegionNavigated_WithoutDispatcherAccess_MarshalsToDispatcher()
    {
        StaHelper.Run(() =>
        {
            var dispatcher = new FakeDispatcher(hasAccess: false);
            var host = new TestHost(dispatcher);
            var service = new FakeNavigationService();
            var locator = new ViewLocator();
            locator.Register<TestView, StubViewModel>();
            host.ViewLocator = locator;
            host.NavigationService = service;

            service.RaiseNavigated("MainRegion", new StubViewModel());

            Assert.Null(host.Content);
            Assert.Equal(1, dispatcher.PostedCount);

            dispatcher.RunPosted();

            var view = Assert.IsType<TestView>(host.Content);
            Assert.IsType<StubViewModel>(view.DataContext);
        });
    }

    [Fact]
    public void RegionNavigated_WithoutViewLocator_ReportsFailure()
    {
        StaHelper.Run(() =>
        {
            var host = new TestHost(new FakeDispatcher());
            var service = new FakeNavigationService();
            host.NavigationService = service; // No ViewLocator.

            var vm = new StubViewModel();
            service.RaiseNavigated("MainRegion", vm);

            var failure = Assert.Single(host.Failures);
            Assert.Same(vm, failure.ViewModel);
            var ex = Assert.IsType<InvalidOperationException>(failure.Error);
            Assert.Contains(nameof(NavigationHost.ViewLocator), ex.Message);
            Assert.Null(host.Content);
        });
    }

    [Fact]
    public void RegionNavigated_UnregisteredViewModel_ReportsFailureAndKeepsContent()
    {
        StaHelper.Run(() =>
        {
            var (host, _, service) = CreateHost();
            var vm = new StubViewModel();
            service.RaiseNavigated("MainRegion", vm);
            Assert.IsType<TestView>(host.Content);

            var unknown = new OtherViewModel();
            service.RaiseNavigated("MainRegion", unknown);

            // Previous content is kept.
            Assert.Same(vm, ((FrameworkElement)host.Content).DataContext);
            var failure = Assert.Single(host.Failures);
            Assert.Same(unknown, failure.ViewModel);
            Assert.IsType<InvalidOperationException>(failure.Error);
        });
    }

    [Fact]
    public void RegionNavigated_ThrowingFactory_ReportsFailureAndKeepsContent()
    {
        StaHelper.Run(() =>
        {
            var host = new TestHost(new FakeDispatcher());
            var service = new FakeNavigationService();
            var locator = new ViewLocator();
            var expected = new InvalidOperationException("factory boom");
            locator.Register<StubViewModel>(_ => throw expected);
            host.ViewLocator = locator;
            host.NavigationService = service;

            service.RaiseNavigated("MainRegion", new StubViewModel());

            Assert.Null(host.Content);
            var failure = Assert.Single(host.Failures);
            Assert.Same(expected, failure.Error);
        });
    }

    [Fact]
    public void KeepAlive_ReusesSameViewInstance()
    {
        StaHelper.Run(() =>
        {
            var (host, _, service) = CreateHost();
            var vm = new StubViewModel();

            service.RaiseNavigated("MainRegion", vm);
            var first = host.Content;
            service.RaiseNavigated("MainRegion", vm);

            Assert.Same(first, host.Content);
        });
    }

    [Fact]
    public void Dispose_UnsubscribesAndIsIdempotent()
    {
        StaHelper.Run(() =>
        {
            var (host, dispatcher, service) = CreateHost();
            host.Dispose();
            host.Dispose(); // Must not throw.

            service.RaiseNavigated("MainRegion", new StubViewModel());

            Assert.Null(host.Content);
            Assert.Equal(0, dispatcher.PostedCount);
        });
    }

    [Fact]
    public void Dispose_ClearsNavigationServiceReference()
    {
        StaHelper.Run(() =>
        {
            var (host, _, _) = CreateHost();

            host.Dispose();

            Assert.Null(host.NavigationService);
        });
    }

    [Fact]
    public void NavigationService_Reassign_ReplacesSubscription()
    {
        StaHelper.Run(() =>
        {
            var (host, _, first) = CreateHost();
            var second = new FakeNavigationService();
            host.NavigationService = second;

            first.RaiseNavigated("MainRegion", new StubViewModel());
            Assert.Null(host.Content);

            second.RaiseNavigated("MainRegion", new StubViewModel());
            Assert.IsType<TestView>(host.Content);
        });
    }

    [Fact]
    public void NavigationService_SetNull_Detaches()
    {
        StaHelper.Run(() =>
        {
            var (host, _, service) = CreateHost();
            host.NavigationService = null;

            service.RaiseNavigated("MainRegion", new StubViewModel());

            Assert.Null(host.Content);
        });
    }

    [Fact]
    public void NavigationService_SetAfterDispose_ThrowsObjectDisposedException()
    {
        StaHelper.Run(() =>
        {
            var (host, _, _) = CreateHost();
            host.Dispose();

            Assert.Throws<ObjectDisposedException>(() => host.NavigationService = new FakeNavigationService());
        });
    }

    [Fact]
    public void RegionName_DefaultsToMainRegion_AndIsSettable()
    {
        StaHelper.Run(() =>
        {
            var host = new TestHost(new FakeDispatcher());

            Assert.Equal("MainRegion", host.RegionName);

            host.RegionName = "SidePanel";

            Assert.Equal("SidePanel", host.RegionName);
        });
    }

    [Fact]
    public void RegionName_Change_TakesEffectImmediately()
    {
        StaHelper.Run(() =>
        {
            var (host, _, service) = CreateHost();
            host.RegionName = "SidePanel";

            service.RaiseNavigated("MainRegion", new StubViewModel());
            Assert.Null(host.Content);

            service.RaiseNavigated("SidePanel", new StubViewModel());
            Assert.IsType<TestView>(host.Content);
        });
    }
}
