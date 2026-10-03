using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.WPF.Extensions;
using ElegantSeries.Flow.WPF.Locating;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// Windows-only tests for <see cref="FlowViewServiceExtensions"/>.
/// </summary>
public sealed class FlowViewServiceExtensionsTests
{
    private sealed class TestView : FrameworkElement
    {
    }

    [Fact]
    public void AddFlowViews_RegistersSingletonViewLocator()
    {
        var services = new ServiceCollection();
        services.AddFlowViews();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IViewLocator>();
        var second = provider.GetRequiredService<IViewLocator>();

        Assert.IsType<ViewLocator>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddFlowViews_AppliesConfigureAction()
    {
        var services = new ServiceCollection();
        services.AddFlowViews(views => views.Register<TestView, StubViewModel>());

        using var provider = services.BuildServiceProvider();
        var locator = provider.GetRequiredService<IViewLocator>();

        Assert.True(locator.IsRegistered<StubViewModel>());
    }

    [Fact]
    public void AddFlowViews_WithoutConfigure_DoesNotOverrideExistingRegistration()
    {
        var services = new ServiceCollection();
        var custom = new ViewLocator();
        services.AddSingleton<IViewLocator>(custom);
        services.AddFlowViews();

        using var provider = services.BuildServiceProvider();

        Assert.Same(custom, provider.GetRequiredService<IViewLocator>());
    }

    [Fact]
    public void AddFlowViews_WithConfigure_ReplacesExistingRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IViewLocator>(new ViewLocator());
        services.AddFlowViews(views => views.Register<TestView, StubViewModel>());

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IViewLocator>();

        // Last registration wins: the configured locator is the one resolved.
        Assert.True(resolved.IsRegistered<StubViewModel>());
    }

    [Fact]
    public void AddFlowViews_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => FlowViewServiceExtensions.AddFlowViews(null!));
    }

    [Fact]
    public void AddFlowViews_ReturnsServices_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddFlowViews();

        Assert.Same(services, result);
    }

    [Fact]
    public void RegisterTransient_RegistersViewModelAsTransient_AndMapsView()
    {
        var services = new ServiceCollection();
        services.AddFlowViews(views => views.RegisterTransient<TestView, StubViewModel>());

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<StubViewModel>();
        var second = provider.GetRequiredService<StubViewModel>();

        Assert.NotSame(first, second);
        Assert.True(provider.GetRequiredService<IViewLocator>().IsRegistered<StubViewModel>());
    }

    [Fact]
    public void RegisterSingleton_RegistersViewModelAsSingleton_AndMapsView()
    {
        var services = new ServiceCollection();
        services.AddFlowViews(views => views.RegisterSingleton<TestView, StubViewModel>());

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<StubViewModel>();
        var second = provider.GetRequiredService<StubViewModel>();

        Assert.Same(first, second);
        Assert.True(provider.GetRequiredService<IViewLocator>().IsRegistered<StubViewModel>());
    }

    [Fact]
    public void RegisterTransient_OnDirectlyConstructedLocator_ThrowsInvalidOperationException()
    {
        var locator = new ViewLocator();

        var ex = Assert.Throws<InvalidOperationException>(
            () => locator.RegisterTransient<TestView, StubViewModel>());

        Assert.Contains("AddFlowViews", ex.Message);
        Assert.False(locator.IsRegistered<StubViewModel>());
    }

    [Fact]
    public void RegisterSingleton_OnDirectlyConstructedLocator_ThrowsInvalidOperationException()
    {
        var locator = new ViewLocator();

        Assert.Throws<InvalidOperationException>(
            () => locator.RegisterSingleton<TestView, StubViewModel>());
    }

    [Fact]
    public void AddFlowViews_ConfigureRunsEagerly_BeforeProviderIsBuilt()
    {
        var services = new ServiceCollection();
        var ran = false;
        services.AddFlowViews(_ => ran = true);

        // Eager: registrations (including ViewModel DI lifetimes) must take
        // effect before BuildServiceProvider(), not on first resolution.
        Assert.True(ran);
    }

    [Fact]
    public void RegisterTransient_DuplicateMapping_ThrowsWithoutWritingDiDescriptor()
    {
        var services = new ServiceCollection();
        IViewLocator? locator = null;
        services.AddFlowViews(views =>
        {
            locator = views;
            views.Register<TestView, StubViewModel>();
        });
        var before = services.Count;

        Assert.Throws<InvalidOperationException>(
            () => locator!.RegisterTransient<TestView, StubViewModel>());

        // The view mapping threw first, so no DI descriptor was written: no half-state.
        Assert.Equal(before, services.Count);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(StubViewModel));
    }

    [Fact]
    public void Register_DuplicateAfterRegisterTransient_ThrowsWithoutChangingServices()
    {
        var services = new ServiceCollection();
        IViewLocator? locator = null;
        services.AddFlowViews(views =>
        {
            locator = views;
            views.RegisterTransient<TestView, StubViewModel>();
        });
        var before = services.Count;

        Assert.Throws<InvalidOperationException>(
            () => locator!.Register<TestView, StubViewModel>());

        Assert.Equal(before, services.Count);
    }
}