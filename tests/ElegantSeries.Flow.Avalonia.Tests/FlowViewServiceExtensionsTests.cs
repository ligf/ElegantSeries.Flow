using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Avalonia.Extensions;
using ElegantSeries.Flow.Avalonia.Locating;

namespace ElegantSeries.Flow.Avalonia.Tests;

public sealed class FlowViewServiceExtensionsTests
{
    [Fact]
    public void AddFlowViews_RegistersViewLocatorSingleton()
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
    public void AddFlowViews_WithConfigure_AppliesRegistrations()
    {
        var services = new ServiceCollection();
        services.AddFlowViews(locator => locator.Register<TestView, TestViewModel>());

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IViewLocator>();

        Assert.True(resolved.IsRegistered<TestViewModel>());
        Assert.IsType<TestView>(resolved.CreateView(new TestViewModel()));
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
        services.AddFlowViews(locator => locator.Register<TestView, TestViewModel>());

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IViewLocator>();

        // Last registration wins: the configured locator is the one resolved.
        Assert.True(resolved.IsRegistered<TestViewModel>());
    }
}
