using Avalonia.Controls;

namespace ElegantSeries.Flow.Avalonia.Tests;

public sealed class ViewLocatorTests
{
    [Fact]
    public void Register_ThenCreateView_ReturnsNewViewOfRegisteredType()
    {
        var locator = new ViewLocator();
        locator.Register<TestView, TestViewModel>();

        var first = locator.CreateView(new TestViewModel());
        var second = locator.CreateView(new TestViewModel());

        Assert.IsType<TestView>(first);
        Assert.IsType<TestView>(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Register_DuplicateRegistration_ThrowsInvalidOperationException()
    {
        var locator = new ViewLocator();
        locator.Register<TestView, TestViewModel>();

        Assert.Throws<InvalidOperationException>(() => locator.Register<TestView, TestViewModel>());
    }

    [Fact]
    public void CreateView_UnregisteredViewModel_ThrowsWithTypeName()
    {
        var locator = new ViewLocator();

        var ex = Assert.Throws<InvalidOperationException>(
            () => locator.CreateView(new UnregisteredViewModel()));

        Assert.Contains(typeof(UnregisteredViewModel).FullName!, ex.Message);
    }

    [Fact]
    public void CreateView_NullViewModel_ThrowsArgumentNullException()
    {
        var locator = new ViewLocator();

        Assert.Throws<ArgumentNullException>(() => locator.CreateView(null!));
    }

    [Fact]
    public void Register_NullFactory_ThrowsArgumentNullException()
    {
        var locator = new ViewLocator();

        Assert.Throws<ArgumentNullException>(
            () => locator.Register<TestViewModel>((Func<TestViewModel, Control>)null!));
    }

    [Fact]
    public void Register_FactoryOverload_PassesViewModelInstanceToFactory()
    {
        var locator = new ViewLocator();
        var vm = new TestViewModel();
        TestViewModel? received = null;
        locator.Register<TestViewModel>(candidate =>
        {
            received = candidate;
            return new TestView();
        });

        var view = locator.CreateView(vm);

        Assert.IsType<TestView>(view);
        Assert.Same(vm, received);
    }

    [Fact]
    public void CreateView_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
        var locator = new ViewLocator();
        locator.Register<TestViewModel>(_ => null!);

        Assert.Throws<InvalidOperationException>(() => locator.CreateView(new TestViewModel()));
    }

    [Fact]
    public void CreateView_FactoryThrows_PropagatesAndKeepsNoState()
    {
        var locator = new ViewLocator();
        locator.Register<TestViewModel>(_ => throw new InvalidOperationException("boom"));

        Assert.Throws<InvalidOperationException>(() => locator.CreateView(new TestViewModel()));
        // The locator is still usable afterwards: registration was not corrupted.
        Assert.True(locator.IsRegistered<TestViewModel>());
    }

    [Fact]
    public void CreateView_DerivedViewModelWithoutOwnRegistration_Throws()
    {
        // Lookup is keyed by the exact runtime type: a derived ViewModel does not
        // inherit its base type's registration. This pins the documented behavior.
        var locator = new ViewLocator();
        locator.Register<TestView, TestViewModel>();

        Assert.Throws<InvalidOperationException>(() => locator.CreateView(new DerivedViewModel()));
    }

    [Fact]
    public void IsRegistered_ReturnsFalseBeforeAndTrueAfterRegistration()
    {
        var locator = new ViewLocator();

        Assert.False(locator.IsRegistered<TestViewModel>());

        locator.Register<TestView, TestViewModel>();

        Assert.True(locator.IsRegistered<TestViewModel>());
        Assert.False(locator.IsRegistered<OtherViewModel>());
    }

    [Fact]
    public void Register_ConcurrentDistinctRegistrations_AllSucceed()
    {
        var locator = new ViewLocator();

        Parallel.Invoke(
            () => locator.Register<TestView, TestViewModel>(),
            () => locator.Register<TestView, OtherViewModel>(),
            () => locator.Register<TestView, ThirdViewModel>(),
            () => locator.Register<TestView, FourthViewModel>());

        Assert.True(locator.IsRegistered<TestViewModel>());
        Assert.True(locator.IsRegistered<OtherViewModel>());
        Assert.True(locator.IsRegistered<ThirdViewModel>());
        Assert.True(locator.IsRegistered<FourthViewModel>());
    }
}
