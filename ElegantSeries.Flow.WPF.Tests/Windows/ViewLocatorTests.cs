using System.Windows;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// Windows-only tests for <see cref="ViewLocator"/> (the WPF adapter over
/// <see cref="ViewRegistry{TView}"/>). The registry logic itself is covered by
/// the Linux-runnable <c>ViewRegistryTests</c>; these tests pin the WPF-specific
/// generic constraints and instance creation.
/// </summary>
public sealed class ViewLocatorTests
{
    private sealed class TestView : FrameworkElement
    {
    }

    private sealed class OtherView : FrameworkElement
    {
    }

    [Fact]
    public void Register_ThenCreateView_CreatesViewInstance()
    {
        StaHelper.Run(() =>
        {
            IViewLocator locator = new ViewLocator();
            locator.Register<TestView, StubViewModel>();

            var view = locator.CreateView(new StubViewModel());

            Assert.IsType<TestView>(view);
        });
    }

    [Fact]
    public void Register_Duplicate_ThrowsInvalidOperationException()
    {
        StaHelper.Run(() =>
        {
            IViewLocator locator = new ViewLocator();
            locator.Register<TestView, StubViewModel>();

            var ex = Assert.Throws<InvalidOperationException>(
                () => locator.Register<OtherView, StubViewModel>());

            Assert.Contains(typeof(StubViewModel).FullName!, ex.Message);
        });
    }

    [Fact]
    public void Register_FactoryOverload_UsesFactory()
    {
        StaHelper.Run(() =>
        {
            IViewLocator locator = new ViewLocator();
            var vm = new StubViewModel();
            var view = new TestView();
            StubViewModel? received = null;
            locator.Register<StubViewModel>(v => { received = v; return view; });

            var created = locator.CreateView(vm);

            Assert.Same(view, created);
            Assert.Same(vm, received);
        });
    }

    [Fact]
    public void Register_NullFactory_ThrowsArgumentNullException()
    {
        IViewLocator locator = new ViewLocator();

        Assert.Throws<ArgumentNullException>(() => locator.Register<StubViewModel>(null!));
    }

    [Fact]
    public void CreateView_Unregistered_ThrowsInvalidOperationException()
    {
        IViewLocator locator = new ViewLocator();

        var ex = Assert.Throws<InvalidOperationException>(
            () => locator.CreateView(new StubViewModel()));

        Assert.Contains(typeof(StubViewModel).FullName!, ex.Message);
    }

    [Fact]
    public void CreateView_NullViewModel_ThrowsArgumentNullException()
    {
        IViewLocator locator = new ViewLocator();

        Assert.Throws<ArgumentNullException>(() => locator.CreateView(null!));
    }

    [Fact]
    public void IsRegistered_TracksRegistrations()
    {
        IViewLocator locator = new ViewLocator();

        Assert.False(locator.IsRegistered<StubViewModel>());

        StaHelper.Run(() => locator.Register<TestView, StubViewModel>());

        Assert.True(locator.IsRegistered<StubViewModel>());
        Assert.False(locator.IsRegistered<OtherViewModel>());
    }
}
