using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Avalonia.Extensions;
using ElegantSeries.Flow.Avalonia.Locating;

namespace ElegantSeries.Flow.Avalonia.Tests;

public sealed class FlowViewServiceExtensionsTests
{
    [Fact]
    public void AddElegantSeriesFlowViews_RegistersViewLocatorSingleton()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlowViews();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IViewLocator>();
        var second = provider.GetRequiredService<IViewLocator>();

        Assert.IsType<ViewLocator>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddElegantSeriesFlowViews_WithConfigure_AppliesRegistrations()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlowViews(locator => locator.Register<TestView, TestViewModel>());

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IViewLocator>();

        Assert.True(resolved.IsRegistered<TestViewModel>());
        Assert.IsType<TestView>(resolved.CreateView(new TestViewModel()));
    }

    [Fact]
    public void AddElegantSeriesFlowViews_WithoutConfigure_DoesNotOverrideExistingRegistration()
    {
        var services = new ServiceCollection();
        var custom = new ViewLocator();
        services.AddSingleton<IViewLocator>(custom);
        services.AddElegantSeriesFlowViews();

        using var provider = services.BuildServiceProvider();

        Assert.Same(custom, provider.GetRequiredService<IViewLocator>());
    }

    [Fact]
    public void AddElegantSeriesFlowViews_WithConfigure_ReplacesExistingRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IViewLocator>(new ViewLocator());
        services.AddElegantSeriesFlowViews(locator => locator.Register<TestView, TestViewModel>());

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IViewLocator>();

        // Last registration wins: the configured locator is the one resolved.
        Assert.True(resolved.IsRegistered<TestViewModel>());
    }

    [Fact]
    public void RegisterTransient_RegistersViewModelAsTransient_AndMapsView()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlowViews(views => views.RegisterTransient<TestView, TestViewModel>());

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
        services.AddElegantSeriesFlowViews(views => views.RegisterSingleton<TestView, TestViewModel>());

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

        Assert.Contains("AddElegantSeriesFlowViews", ex.Message);
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
    public void AddElegantSeriesFlowViews_ConfigureRunsEagerly_BeforeProviderIsBuilt()
    {
        var services = new ServiceCollection();
        var ran = false;
        services.AddElegantSeriesFlowViews(_ => ran = true);

        // Eager: registrations (including ViewModel DI lifetimes) must take
        // effect before BuildServiceProvider(), not on first resolution.
        Assert.True(ran);
    }

    [Fact]
    public void RegisterTransient_DuplicateMapping_ThrowsWithoutWritingDiDescriptor()
    {
        var services = new ServiceCollection();
        IViewLocator? locator = null;
        services.AddElegantSeriesFlowViews(views =>
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
        services.AddElegantSeriesFlowViews(views =>
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
