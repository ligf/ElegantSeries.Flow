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
    public void AddElegantSeriesFlowViews_RegistersSingletonViewLocator()
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
    public void AddElegantSeriesFlowViews_AppliesConfigureAction()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlowViews(views => views.Register<TestView, StubViewModel>());

        using var provider = services.BuildServiceProvider();
        var locator = provider.GetRequiredService<IViewLocator>();

        Assert.True(locator.IsRegistered<StubViewModel>());
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
        services.AddElegantSeriesFlowViews(views => views.Register<TestView, StubViewModel>());

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IViewLocator>();

        // Last registration wins: the configured locator is the one resolved.
        Assert.True(resolved.IsRegistered<StubViewModel>());
    }

    [Fact]
    public void AddElegantSeriesFlowViews_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => FlowViewServiceExtensions.AddElegantSeriesFlowViews(null!));
    }

    [Fact]
    public void AddElegantSeriesFlowViews_ReturnsServices_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddElegantSeriesFlowViews();

        Assert.Same(services, result);
    }

    [Fact]
    public void RegisterTransient_RegistersViewModelAsTransient_AndMapsView()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlowViews(views => views.RegisterTransient<TestView, StubViewModel>());

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
        services.AddElegantSeriesFlowViews(views => views.RegisterSingleton<TestView, StubViewModel>());

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

        Assert.Contains("AddElegantSeriesFlowViews", ex.Message);
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
        services.AddElegantSeriesFlowViews(views =>
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
