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

    [Fact]
    public void RegisterTransient_RegistersViewModelAsTransient_AndMapsView()
    {
        var services = new ServiceCollection();
        services.AddFlowViews(views => views.RegisterTransient<TestView, TestViewModel>());

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<TestViewModel>();
        var second = provider.GetRequiredService<TestViewModel>();

        Assert.NotSame(first, second);
        Assert.True(provider.GetRequiredService<IViewLocator>().IsRegistered<TestViewModel>());
    }

    [Fact]
    public void RegisterSingleton_RegistersViewModelAsSingleton_AndMapsView()
    {
        var services = new ServiceCollection();
        services.AddFlowViews(views => views.RegisterSingleton<TestView, TestViewModel>());

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<TestViewModel>();
        var second = provider.GetRequiredService<TestViewModel>();

        Assert.Same(first, second);
        Assert.True(provider.GetRequiredService<IViewLocator>().IsRegistered<TestViewModel>());
    }

    [Fact]
    public void RegisterTransient_OnDirectlyConstructedLocator_ThrowsInvalidOperationException()
    {
        var locator = new ViewLocator();

        var ex = Assert.Throws<InvalidOperationException>(
            () => locator.RegisterTransient<TestView, TestViewModel>());

        Assert.Contains("AddFlowViews", ex.Message);
        Assert.False(locator.IsRegistered<TestViewModel>());
    }

    [Fact]
    public void RegisterSingleton_OnDirectlyConstructedLocator_ThrowsInvalidOperationException()
    {
        var locator = new ViewLocator();

        Assert.Throws<InvalidOperationException>(
            () => locator.RegisterSingleton<TestView, TestViewModel>());
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
            views.Register<TestView, TestViewModel>();
        });
        var before = services.Count;

        Assert.Throws<InvalidOperationException>(
            () => locator!.RegisterTransient<TestView, TestViewModel>());

        // The view mapping threw first, so no DI descriptor was written: no half-state.
        Assert.Equal(before, services.Count);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(TestViewModel));
    }

    [Fact]
    public void Register_DuplicateAfterRegisterTransient_ThrowsWithoutChangingServices()
    {
        var services = new ServiceCollection();
        IViewLocator? locator = null;
        services.AddFlowViews(views =>
        {
            locator = views;
            views.RegisterTransient<TestView, TestViewModel>();
        });
        var before = services.Count;

        Assert.Throws<InvalidOperationException>(
            () => locator!.Register<TestView, TestViewModel>());

        Assert.Equal(before, services.Count);
    }
}