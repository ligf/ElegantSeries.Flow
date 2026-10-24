using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Hosting;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Tests.Shared;

namespace ElegantSeries.Flow.WPF.Tests.Logic;

/// <summary>
/// Controller-level harness for the shared
/// <see cref="HostIntegrationScenarios"/>: drives a real
/// <see cref="INavigationService"/> (via DI) through a
/// <see cref="NavigationHostController{TView}"/> with <c>object</c>
/// views, mirroring the exact call sequence both real hosts use
/// (attach/detach + refresh on service change, refresh on region
/// change). Runs on Linux, so the WPF stack's host behavior is
/// covered outside Windows CI as well.
/// </summary>
internal sealed class ControllerHostHarness : IHostIntegrationHarness
{
    /// <summary>Stand-in view for the Alpha page; the class name
    /// mirrors the real hosts' view names so the shared scenarios'
    /// view-type assertions apply to this leg too.</summary>
    private sealed class HostAlphaView
    {
    }

    /// <summary>Stand-in view for the Beta page.</summary>
    private sealed class HostBetaView
    {
    }

    private sealed class HostState
    {
        public NavigationHostController<object> Controller = null!;
        public string Region = HostIntegrationScenarios.MainRegion;
        public INavigationViewModel? Current;
        public object? CurrentView;
    }

    private readonly FakeDispatcher _dispatcher = new(hasAccess: false);
    private readonly List<ServiceProvider> _providers = [];

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
        var state = new HostState();
        state.Controller = new NavigationHostController<object>(
            _dispatcher,
            regionNameProvider: () => state.Region,
            viewFactory: ctx => ctx.ViewModel is HostAlphaViewModel ? new HostAlphaView() : new HostBetaView(),
            showView: (view, vm) =>
            {
                state.Current = vm;
                state.CurrentView = view;
            },
            clearView: () =>
            {
                state.Current = null;
                state.CurrentView = null;
            });
        return state;
    }

    public void SetService(object host, INavigationService? service)
    {
        var state = (HostState)host;

        // Mirror the hosts' service-changed sequence exactly.
        if (service is null)
        {
            state.Controller.Detach();
        }
        else
        {
            state.Controller.Attach(service);
        }

        state.Controller.Refresh();
        _dispatcher.RunPosted();
    }

    public void SetRegion(object host, string region)
    {
        var state = (HostState)host;

        // Mirror the hosts: both trim the region name they are given.
        state.Region = region.Trim();
        state.Controller.Refresh();
        _dispatcher.RunPosted();
    }

    public INavigationViewModel? ContentViewModel(object host) => ((HostState)host).Current;

    public object? ContentView(object host) => ((HostState)host).CurrentView;

    public void DisposeHost(object host) => ((HostState)host).Controller.Detach();

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }
}

/// <summary>Runs the shared host integration scenarios against the shared host controller.</summary>
public sealed class HostIntegrationTests
{
    [Fact]
    public async Task NavigateThenAttach_ShowsCurrentPage()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.NavigateThenAttach_ShowsCurrentPage(harness);
    }

    [Fact]
    public async Task AttachThenNavigate_FollowsNavigationAndGoBack()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.AttachThenNavigate_FollowsNavigationAndGoBack(harness);
    }

    [Fact]
    public async Task ServiceSwitch_SyncsToNewServiceAndIgnoresOld()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.ServiceSwitch_SyncsToNewServiceAndIgnoresOld(harness);
    }

    [Fact]
    public async Task Detach_PreservesContentAndStopsUpdates()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.Detach_PreservesContentAndStopsUpdates(harness);
    }

    [Fact]
    public async Task RegionRename_SyncsToTheNewRegion()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.RegionRename_SyncsToTheNewRegion(harness);
    }

    [Fact]
    public async Task RegionRename_WithPadding_StillSyncs()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.RegionRename_WithPadding_StillSyncs(harness);
    }

    [Fact]
    public async Task OtherRegionNavigation_IsIgnored()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.OtherRegionNavigation_IsIgnored(harness);
    }

    [Fact]
    public async Task Dispose_StopsUpdates()
    {
        using var harness = new ControllerHostHarness();
        await HostIntegrationScenarios.Dispose_StopsUpdates(harness);
    }
}
