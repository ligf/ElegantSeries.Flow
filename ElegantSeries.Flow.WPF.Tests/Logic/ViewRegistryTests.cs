using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.WPF.Tests.Logic;

/// <summary>
/// Linux-runnable tests for <see cref="ViewRegistry{TView}"/> (compiled from the
/// library source; <c>object</c> stands in for the view type).
/// </summary>
public sealed class ViewRegistryTests
{
    private sealed class DerivedViewModel : StubViewModel
    {
    }

    [Fact]
    public void Register_ThenCreateView_ReturnsFactoryResult()
    {
        var registry = new ViewRegistry<object>();
        var view = new object();
        registry.Register<StubViewModel>(_ => view);

        var created = registry.CreateView(new StubViewModel());

        Assert.Same(view, created);
    }

    [Fact]
    public void Register_PassesViewModelInstanceToFactory()
    {
        var registry = new ViewRegistry<object>();
        var vm = new StubViewModel();
        BaseViewModel? received = null;
        registry.Register<StubViewModel>(v => { received = v; return new object(); });

        registry.CreateView(vm);

        Assert.Same(vm, received);
    }

    [Fact]
    public void Register_Duplicate_ThrowsInvalidOperationException()
    {
        var registry = new ViewRegistry<object>();
        registry.Register<StubViewModel>(_ => new object());

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register<StubViewModel>(_ => new object()));

        Assert.Contains(typeof(StubViewModel).FullName!, ex.Message);
    }

    [Fact]
    public void Register_NullFactory_ThrowsArgumentNullException()
    {
        var registry = new ViewRegistry<object>();

        Assert.Throws<ArgumentNullException>(() => registry.Register<StubViewModel>(null!));
    }

    [Fact]
    public void CreateView_Unregistered_ThrowsInvalidOperationException()
    {
        var registry = new ViewRegistry<object>();

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.CreateView(new OtherViewModel()));

        Assert.Contains(typeof(OtherViewModel).FullName!, ex.Message);
    }

    [Fact]
    public void CreateView_NullViewModel_ThrowsArgumentNullException()
    {
        var registry = new ViewRegistry<object>();

        Assert.Throws<ArgumentNullException>(() => registry.CreateView(null!));
    }

    [Fact]
    public void CreateView_UsesExactRuntimeType()
    {
        var registry = new ViewRegistry<object>();
        registry.Register<StubViewModel>(_ => new object());

        // A derived ViewModel does not match the base-type registration.
        Assert.Throws<InvalidOperationException>(() => registry.CreateView(new DerivedViewModel()));
    }

    [Fact]
    public void IsRegistered_TracksRegistrations()
    {
        var registry = new ViewRegistry<object>();

        Assert.False(registry.IsRegistered<StubViewModel>());

        registry.Register<StubViewModel>(_ => new object());

        Assert.True(registry.IsRegistered<StubViewModel>());
        Assert.False(registry.IsRegistered<OtherViewModel>());
    }

    [Fact]
    public void Register_ConcurrentDuplicate_ExactlyOneWins()
    {
        var registry = new ViewRegistry<object>();
        int succeeded = 0;
        int rejected = 0;

        Parallel.For(0, 16, _ =>
        {
            try
            {
                registry.Register<StubViewModel>(_ => new object());
                Interlocked.Increment(ref succeeded);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref rejected);
            }
        });

        Assert.Equal(1, succeeded);
        Assert.Equal(15, rejected);
        Assert.True(registry.IsRegistered<StubViewModel>());
    }
}
