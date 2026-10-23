using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests.Shared;

/// <summary>The two pages the host integration scenarios navigate between.</summary>
public enum HostPage
{
    Alpha,
    Beta,
}

/// <summary>First scenario page. Shared by every harness; each platform
/// registers its own view for this ViewModel type.</summary>
public sealed class HostAlphaViewModel : NavigationViewModelBase
{
}

/// <summary>Second scenario page.</summary>
public sealed class HostBetaViewModel : NavigationViewModelBase
{
}

/// <summary>
/// Platform adapter for <see cref="HostIntegrationScenarios"/>: each UI
/// test project implements this once for its own host (Avalonia
/// <c>NavigationHost</c>, WPF <c>NavigationHost</c>, and the WPF
/// controller-level harness), so the identical scenario code runs
/// against every host and any behavioral divergence fails somewhere.
/// Hosts are opaque <see cref="object"/> handles owned by the harness.
/// </summary>
public interface IHostIntegrationHarness : IDisposable
{
    /// <summary>Creates a real navigation service with the Alpha and
    /// Beta pages registered. No navigation has happened yet.</summary>
    INavigationService CreateService();

    /// <summary>Navigates the service to a page in a region (default
    /// operation and retention) and awaits the outcome.</summary>
    Task NavigateAsync(INavigationService service, HostPage page, string region);

    /// <summary>Goes back in a region and awaits the outcome.</summary>
    Task GoBackAsync(INavigationService service, string region);

    /// <summary>Creates a host for the harness's default region
    /// ("MainRegion") with a view locator configured for both pages.
    /// No navigation service is attached yet.</summary>
    object CreateHost();

    /// <summary>Attaches a navigation service to the host, or detaches
    /// when <paramref name="service"/> is null — the same call a user
    /// makes when they set the host's service property.</summary>
    void SetService(object host, INavigationService? service);

    /// <summary>Points the host at another region — the same call a
    /// user makes when they set the host's region property.</summary>
    void SetRegion(object host, string region);

    /// <summary>The ViewModel the host currently displays, or null
    /// when it shows nothing.</summary>
    INavigationViewModel? ContentViewModel(object host);

    /// <summary>Disposes (or detaches) the host. The content it last
    /// showed must remain readable through
    /// <see cref="ContentViewModel"/> afterwards.</summary>
    void DisposeHost(object host);
}

/// <summary>
/// Host integration scenarios, shared verbatim by the Avalonia and WPF
/// test projects (roadmap Phase 2B #8/#9): attach/detach, service
/// switching, region switching, navigate-before-attach — always
/// against a real <see cref="INavigationService"/>, never a fake, so
/// the host + controller + service integration is what is tested.
/// </summary>
public static class HostIntegrationScenarios
{
    public const string MainRegion = "MainRegion";
    public const string SecondaryRegion = "Secondary";

    public static async Task NavigateThenAttach_ShowsCurrentPage(IHostIntegrationHarness harness)
    {
        var service = harness.CreateService();
        await harness.NavigateAsync(service, HostPage.Alpha, MainRegion);
        var alpha = service.GetCurrentViewModel(MainRegion);
        Assert.NotNull(alpha);

        var host = harness.CreateHost();
        harness.SetService(host, service);

        Assert.Same(alpha, harness.ContentViewModel(host));
    }

    public static async Task AttachThenNavigate_FollowsNavigationAndGoBack(IHostIntegrationHarness harness)
    {
        var service = harness.CreateService();
        var host = harness.CreateHost();
        harness.SetService(host, service);
        Assert.Null(harness.ContentViewModel(host));

        await harness.NavigateAsync(service, HostPage.Alpha, MainRegion);
        var alpha = service.GetCurrentViewModel(MainRegion);
        Assert.Same(alpha, harness.ContentViewModel(host));

        await harness.NavigateAsync(service, HostPage.Beta, MainRegion);
        Assert.Same(service.GetCurrentViewModel(MainRegion), harness.ContentViewModel(host));

        await harness.GoBackAsync(service, MainRegion);
        Assert.Same(alpha, harness.ContentViewModel(host));
    }

    public static async Task ServiceSwitch_SyncsToNewServiceAndIgnoresOld(IHostIntegrationHarness harness)
    {
        var first = harness.CreateService();
        var second = harness.CreateService();
        await harness.NavigateAsync(first, HostPage.Alpha, MainRegion);
        await harness.NavigateAsync(second, HostPage.Alpha, MainRegion);
        await harness.NavigateAsync(second, HostPage.Beta, MainRegion);

        var host = harness.CreateHost();
        harness.SetService(host, first);
        Assert.Same(first.GetCurrentViewModel(MainRegion), harness.ContentViewModel(host));

        harness.SetService(host, second);
        var betaOfSecond = second.GetCurrentViewModel(MainRegion);
        Assert.Same(betaOfSecond, harness.ContentViewModel(host));

        // The old service no longer drives the host.
        await harness.NavigateAsync(first, HostPage.Beta, MainRegion);
        Assert.Same(betaOfSecond, harness.ContentViewModel(host));

        // The new service does.
        await harness.GoBackAsync(second, MainRegion);
        Assert.Same(second.GetCurrentViewModel(MainRegion), harness.ContentViewModel(host));
    }

    public static async Task Detach_PreservesContentAndStopsUpdates(IHostIntegrationHarness harness)
    {
        var service = harness.CreateService();
        var host = harness.CreateHost();
        harness.SetService(host, service);
        await harness.NavigateAsync(service, HostPage.Alpha, MainRegion);
        var alpha = service.GetCurrentViewModel(MainRegion);
        Assert.Same(alpha, harness.ContentViewModel(host));

        harness.SetService(host, null);

        // Detaching keeps the last shown page (the host reflects its
        // region only while a service is attached) and later
        // navigations on the detached service are ignored.
        Assert.Same(alpha, harness.ContentViewModel(host));
        await harness.NavigateAsync(service, HostPage.Beta, MainRegion);
        Assert.Same(alpha, harness.ContentViewModel(host));
    }

    public static async Task RegionRename_SyncsToTheNewRegion(IHostIntegrationHarness harness)
    {
        var service = harness.CreateService();
        await harness.NavigateAsync(service, HostPage.Alpha, MainRegion);
        await harness.NavigateAsync(service, HostPage.Beta, SecondaryRegion);

        var host = harness.CreateHost();
        harness.SetService(host, service);
        Assert.Same(service.GetCurrentViewModel(MainRegion), harness.ContentViewModel(host));

        harness.SetRegion(host, SecondaryRegion);
        Assert.Same(service.GetCurrentViewModel(SecondaryRegion), harness.ContentViewModel(host));

        harness.SetRegion(host, "NoSuchRegion");
        Assert.Null(harness.ContentViewModel(host));

        harness.SetRegion(host, MainRegion);
        Assert.Same(service.GetCurrentViewModel(MainRegion), harness.ContentViewModel(host));
    }

    public static async Task OtherRegionNavigation_IsIgnored(IHostIntegrationHarness harness)
    {
        var service = harness.CreateService();
        var host = harness.CreateHost();
        harness.SetService(host, service);

        await harness.NavigateAsync(service, HostPage.Alpha, SecondaryRegion);
        Assert.Null(harness.ContentViewModel(host));

        await harness.NavigateAsync(service, HostPage.Beta, MainRegion);
        Assert.Same(service.GetCurrentViewModel(MainRegion), harness.ContentViewModel(host));
    }

    public static async Task Dispose_StopsUpdates(IHostIntegrationHarness harness)
    {
        var service = harness.CreateService();
        var host = harness.CreateHost();
        harness.SetService(host, service);
        await harness.NavigateAsync(service, HostPage.Alpha, MainRegion);
        var alpha = service.GetCurrentViewModel(MainRegion);
        Assert.Same(alpha, harness.ContentViewModel(host));

        harness.DisposeHost(host);

        await harness.NavigateAsync(service, HostPage.Beta, MainRegion);
        Assert.NotSame(service.GetCurrentViewModel(MainRegion), harness.ContentViewModel(host));
        Assert.Same(alpha, harness.ContentViewModel(host));
    }
}
