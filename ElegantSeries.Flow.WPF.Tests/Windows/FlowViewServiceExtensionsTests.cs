using System.Windows;
using Microsoft.Extensions.DependencyInjection;

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
}
