using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>
/// Pins the relative order of <see cref="INavigationService.ViewModelReleased"/>
/// and <see cref="INavigationService.RegionCacheCleared"/> for the four
/// cache-clearing calls: a clear announces the region cleared only after the
/// entries it removed have been released, uniformly for the single-region
/// and all-region forms.
/// </summary>
public class CacheClearEventOrderTests
{
    public sealed class RootPage : NavigationViewModelBase;

    public sealed class CachedPageA : NavigationViewModelBase;

    public sealed class CachedPageB : NavigationViewModelBase;

    private static (INavigationService Service, ServiceProvider Provider) CreateService()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<RootPage>();
        services.AddTransient<CachedPageA>();
        services.AddTransient<CachedPageB>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<INavigationService>(), provider);
    }

    private static List<string> RecordEvents(INavigationService svc)
    {
        var order = new List<string>();
        svc.SubscribeViewModelReleased((_, vm) => order.Add($"Released:{vm.GetType().Name}"));
        svc.SubscribeRegionCacheCleared(region => order.Add($"Cleared:{region}"));
        return order;
    }

    // Leaves two KeepAlive entries (A and B) in the region's cache, with
    // the root page still on the stack.
    private static async Task SeedTwoCachedPages(INavigationService svc, string region)
    {
        await svc.NavigateToAsync<RootPage>(region);
        await svc.NavigateToAsync<CachedPageA>(region, retention: NavigationRetention.KeepAlive);
        await svc.NavigateToAsync<CachedPageB>(region, retention: NavigationRetention.KeepAlive);
        await svc.GoBackAsync(region);
        await svc.GoBackAsync(region);
    }

    private static async Task SeedOneCachedPage(INavigationService svc, string region)
    {
        await svc.NavigateToAsync<RootPage>(region);
        await svc.NavigateToAsync<CachedPageA>(region, retention: NavigationRetention.KeepAlive);
        await svc.GoBackAsync(region);
    }

    private static void AssertReleasedBeforeCleared(List<string> order, int releasedCount, params string[] clearedRegions)
    {
        Assert.Equal(releasedCount + clearedRegions.Length, order.Count);
        for (var i = 0; i < releasedCount; i++)
            Assert.StartsWith("Released:", order[i]);
        Assert.Equal(
            clearedRegions.Select(r => $"Cleared:{r}").OrderBy(x => x, StringComparer.Ordinal),
            order.Skip(releasedCount).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ClearCache_RaisesReleasedBeforeCacheCleared()
    {
        var (svc, provider) = CreateService();
        using var _p = provider;
        var order = RecordEvents(svc);
        await SeedTwoCachedPages(svc, "MainRegion");

        svc.ClearCache("MainRegion");

        AssertReleasedBeforeCleared(order, 2, "MainRegion");
    }

    [Fact]
    public async Task ClearCacheAsync_RaisesReleasedBeforeCacheCleared()
    {
        var (svc, provider) = CreateService();
        using var _p = provider;
        var order = RecordEvents(svc);
        await SeedTwoCachedPages(svc, "MainRegion");

        await svc.ClearCacheAsync("MainRegion");

        AssertReleasedBeforeCleared(order, 2, "MainRegion");
    }

    [Fact]
    public async Task ClearAllCache_RaisesReleasedBeforeCacheCleared()
    {
        var (svc, provider) = CreateService();
        using var _p = provider;
        var order = RecordEvents(svc);
        await SeedTwoCachedPages(svc, "MainRegion");
        await SeedOneCachedPage(svc, "SecondRegion");

        svc.ClearAllCache();

        AssertReleasedBeforeCleared(order, 3, "MainRegion", "SecondRegion");
    }

    [Fact]
    public async Task ClearAllCacheAsync_RaisesReleasedBeforeCacheCleared()
    {
        var (svc, provider) = CreateService();
        using var _p = provider;
        var order = RecordEvents(svc);
        await SeedTwoCachedPages(svc, "MainRegion");
        await SeedOneCachedPage(svc, "SecondRegion");

        await svc.ClearAllCacheAsync();

        AssertReleasedBeforeCleared(order, 3, "MainRegion", "SecondRegion");
    }
}
