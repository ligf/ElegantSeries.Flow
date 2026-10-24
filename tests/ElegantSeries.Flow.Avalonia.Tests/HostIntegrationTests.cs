using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Tests.Shared;
using ElegantSeries.Flow.Avalonia.Hosting;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Locating;

namespace ElegantSeries.Flow.Avalonia.Tests;

/// <summary>View for <see cref="HostAlphaViewModel"/> in the host integration scenarios.</summary>
internal sealed class HostAlphaView : Control
{
}

/// <summary>View for <see cref="HostBetaViewModel"/> in the host integration scenarios.</summary>
internal sealed class HostBetaView : Control
{
}

/// <summary>
/// Avalonia harness for the shared <see cref="HostIntegrationScenarios"/>:
/// a real <see cref="NavigationService"/> (via DI) driving a real
/// <see cref="NavigationHost"/> through a real <see cref="ViewLocator"/>.
/// The fake dispatcher reports no UI-thread access, so event-driven
/// updates queue and are pumped explicitly after each operation —
/// the same marshaling the real dispatcher performs, made synchronous.
/// </summary>
internal sealed class AvaloniaHostHarness : IHostIntegrationHarness
{
    private sealed class IntegrationHost : NavigationHost
    {
        public IntegrationHost(ElegantSeries.Flow.Avalonia.Threading.IDispatcher dispatcher)
            : base(dispatcher)
        {
        }
    }

    private readonly FakeDispatcher _dispatcher = new() { IsOnUiThread = false };
    private readonly List<ServiceProvider> _providers = [];
    private readonly List<NavigationHost> _hosts = [];

    public INavigationService CreateService()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HostAlphaViewModel>();
        services.AddTransient<HostBetaViewModel>();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<INavigationService>();
    }

    public async Task NavigateAsync(INavigationService service, HostPage page, string region)
    {
        if (page == HostPage.Alpha)
        {
            await service.NavigateToAsync<HostAlphaViewModel>(regionName: region);
        }
        else
        {
            await service.NavigateToAsync<HostBetaViewModel>(regionName: region);
        }

        _dispatcher.RunPosted();
    }

    public async Task GoBackAsync(INavigationService service, string region)
    {
        await service.GoBackAsync(region);
        _dispatcher.RunPosted();
    }

    public object CreateHost()
    {
        var locator = new ViewLocator();
        locator.Register<HostAlphaView, HostAlphaViewModel>();
        locator.Register<HostBetaView, HostBetaViewModel>();
        var host = new IntegrationHost(_dispatcher) { ViewLocator = locator };
        _hosts.Add(host);
        return host;
    }

    public void SetService(object host, INavigationService? service)
    {
        ((NavigationHost)host).NavigationService = service;
        _dispatcher.RunPosted();
    }

    public void SetRegion(object host, string region)
    {
        ((NavigationHost)host).RegionName = region;
        _dispatcher.RunPosted();
    }

    public INavigationViewModel? ContentViewModel(object host) =>
        (((NavigationHost)host).Content as Control)?.DataContext as INavigationViewModel;

    public object? ContentView(object host) => ((NavigationHost)host).Content;

    public void DisposeHost(object host) => ((NavigationHost)host).Dispose();

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }
}

/// <summary>Runs the shared host integration scenarios against the Avalonia host.</summary>
public sealed class HostIntegrationTests
{
    private sealed class TestHost : NavigationHost
    {
        public TestHost(ElegantSeries.Flow.Avalonia.Threading.IDispatcher dispatcher)
            : base(dispatcher)
        {
        }
    }

    [Fact]
    public async Task NavigateThenAttach_ShowsCurrentPage()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.NavigateThenAttach_ShowsCurrentPage(harness);
    }

    [Fact]
    public async Task RegisterFactory_HostDeliversRegionAndParameter_FromRealService()
    {
        // End-to-end: real service -> real host -> real locator, navigating
        // with a parameter into a non-default region. The factory must
        // receive the context of the page actually navigated to.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HostAlphaViewModel>();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<INavigationService>();

        ViewCreationContext? received = null;
        var locator = new ViewLocator();
        locator.RegisterFactory<HostAlphaViewModel>(ctx => { received = ctx; return new HostAlphaView(); });

        var dispatcher = new FakeDispatcher { IsOnUiThread = false };
        using var host = new TestHost(dispatcher) { ViewLocator = locator, RegionName = "Sidebar" };
        host.NavigationService = service;

        Assert.True(await service.NavigateToAsync<HostAlphaViewModel, string>(
            "alpha-param", regionName: "Sidebar",
            cancellationToken: TestContext.Current.CancellationToken));
        dispatcher.RunPosted();

        Assert.NotNull(received);
        Assert.Same(service.GetCurrentViewModel("Sidebar"), received!.ViewModel);
        Assert.Equal("Sidebar", received.RegionName);
        Assert.Equal("alpha-param", received.Parameter);
        Assert.Same(received.ViewModel, ((Control)host.Content!).DataContext);
    }

    [Fact]
    public async Task AttachThenNavigate_FollowsNavigationAndGoBack()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.AttachThenNavigate_FollowsNavigationAndGoBack(harness);
    }

    [Fact]
    public async Task ServiceSwitch_SyncsToNewServiceAndIgnoresOld()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.ServiceSwitch_SyncsToNewServiceAndIgnoresOld(harness);
    }

    [Fact]
    public async Task Detach_PreservesContentAndStopsUpdates()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.Detach_PreservesContentAndStopsUpdates(harness);
    }

    [Fact]
    public async Task RegionRename_SyncsToTheNewRegion()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.RegionRename_SyncsToTheNewRegion(harness);
    }

    [Fact]
    public async Task RegionRename_WithPadding_StillSyncs()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.RegionRename_WithPadding_StillSyncs(harness);
    }

    [Fact]
    public async Task OtherRegionNavigation_IsIgnored()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.OtherRegionNavigation_IsIgnored(harness);
    }

    [Fact]
    public async Task Dispose_StopsUpdates()
    {
        using var harness = new AvaloniaHostHarness();
        await HostIntegrationScenarios.Dispose_StopsUpdates(harness);
    }
}
