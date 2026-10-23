using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Tests.Shared;
using ElegantSeries.Flow.WPF.Hosting;
using ElegantSeries.Flow.WPF.Locating;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>View for <see cref="HostAlphaViewModel"/> in the host integration scenarios.</summary>
internal sealed class HostAlphaView : FrameworkElement
{
}

/// <summary>View for <see cref="HostBetaViewModel"/> in the host integration scenarios.</summary>
internal sealed class HostBetaView : FrameworkElement
{
}

/// <summary>
/// A dedicated STA thread running a WPF dispatcher loop. Work posted
/// through <see cref="RunAsync"/> executes on that thread with the
/// dispatcher synchronization context installed, so awaits resume on
/// the STA thread exactly as in a real WPF application, and posts
/// from the navigation host (BeginInvoke) interleave in FIFO order
/// with the scenario's own steps.
/// </summary>
internal sealed class StaDispatcherThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Dispatcher _dispatcher = null!;

    public StaDispatcherThread()
    {
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public Task RunAsync(Func<Task> work)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await work();
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }));
        return completion.Task;
    }

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join();
        _ready.Dispose();
    }
}

/// <summary>
/// WPF harness for the shared <see cref="HostIntegrationScenarios"/>:
/// a real <see cref="INavigationService"/> (via DI) driving a real
/// <see cref="NavigationHost"/> (public constructor, real
/// <c>WpfDispatcher</c>) through a real <see cref="ViewLocator"/>,
/// with every host interaction marshaled to the STA dispatcher
/// thread. Runs on Windows CI.
/// </summary>
internal sealed class WpfHostHarness : IHostIntegrationHarness
{
    private readonly StaDispatcherThread _sta = new();
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

    public Task NavigateAsync(INavigationService service, HostPage page, string region) =>
        _sta.RunAsync(() => page == HostPage.Alpha
            ? service.NavigateToAsync<HostAlphaViewModel>(regionName: region)
            : service.NavigateToAsync<HostBetaViewModel>(regionName: region));

    public Task GoBackAsync(INavigationService service, string region) =>
        _sta.RunAsync(() => service.GoBackAsync(region));

    public object CreateHost()
    {
        NavigationHost? host = null;
        _sta.RunAsync(() =>
        {
            var locator = new ViewLocator();
            locator.Register<HostAlphaView, HostAlphaViewModel>();
            locator.Register<HostBetaView, HostBetaViewModel>();
            host = new NavigationHost { ViewLocator = locator };
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
        return host!;
    }

    public void SetService(object host, INavigationService? service) =>
        _sta.RunAsync(() =>
        {
            ((NavigationHost)host).NavigationService = service;
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    public void SetRegion(object host, string region) =>
        _sta.RunAsync(() =>
        {
            ((NavigationHost)host).RegionName = region;
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    public INavigationViewModel? ContentViewModel(object host)
    {
        INavigationViewModel? current = null;
        _sta.RunAsync(() =>
        {
            current = (((NavigationHost)host).Content as FrameworkElement)?.DataContext as INavigationViewModel;
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
        return current;
    }

    public void DisposeHost(object host) =>
        _sta.RunAsync(() =>
        {
            ((NavigationHost)host).Dispose();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    public void Dispose()
    {
        _sta.Dispose();
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }
}

/// <summary>Runs the shared host integration scenarios against the WPF host.</summary>
public sealed class HostIntegrationTests
{
    [Fact]
    public async Task NavigateThenAttach_ShowsCurrentPage()
    {
        using var harness = new WpfHostHarness();
        await HostIntegrationScenarios.NavigateThenAttach_ShowsCurrentPage(harness);
    }

    [Fact]
    public async Task AttachThenNavigate_FollowsNavigationAndGoBack()
    {
        using var harness = new WpfHostHarness();
        await HostIntegrationScenarios.AttachThenNavigate_FollowsNavigationAndGoBack(harness);
    }

    [Fact]
    public async Task ServiceSwitch_SyncsToNewServiceAndIgnoresOld()
    {
        using var harness = new WpfHostHarness();
        await HostIntegrationScenarios.ServiceSwitch_SyncsToNewServiceAndIgnoresOld(harness);
    }

    [Fact]
    public async Task Detach_PreservesContentAndStopsUpdates()
    {
        using var harness = new WpfHostHarness();
        await HostIntegrationScenarios.Detach_PreservesContentAndStopsUpdates(harness);
    }

    [Fact]
    public async Task RegionRename_SyncsToTheNewRegion()
    {
        using var harness = new WpfHostHarness();
        await HostIntegrationScenarios.RegionRename_SyncsToTheNewRegion(harness);
    }

    [Fact]
    public async Task OtherRegionNavigation_IsIgnored()
    {
        using var harness = new WpfHostHarness();
        await HostIntegrationScenarios.OtherRegionNavigation_IsIgnored(harness);
    }

    [Fact]
    public async Task Dispose_StopsUpdates()
    {
        using var harness = new WpfHostHarness();
        await HostIntegrationScenarios.Dispose_StopsUpdates(harness);
    }
}
