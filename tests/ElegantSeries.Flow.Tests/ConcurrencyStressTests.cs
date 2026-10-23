using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>Per-provider counters for <see cref="PageScopeMarker"/>:
/// a singleton in the test's own service provider, so concurrent tests
/// can never pollute each other's accounting.</summary>
public sealed class PageScopeLedger
{
    private int _created;
    private int _disposed;

    public (int Created, int Disposed) Snapshot() =>
        (Volatile.Read(ref _created), Volatile.Read(ref _disposed));

    internal void RecordCreated() => Interlocked.Increment(ref _created);

    internal void RecordDisposed() => Interlocked.Increment(ref _disposed);
}

/// <summary>
/// Scoped marker: exactly one instance is created per page scope that
/// produces a ViewModel, and DI disposes it with that scope. Comparing
/// the ledger's created/disposed counts at quiescence audits the
/// framework's scope ownership under concurrency — every page scope
/// ever created must be disposed exactly once (DI itself guarantees
/// at-most-once per instance, so equality proves no scope leaked).
/// </summary>
public sealed class PageScopeMarker : IDisposable
{
    private readonly PageScopeLedger _ledger;

    public PageScopeMarker(PageScopeLedger ledger)
    {
        _ledger = ledger;
        _ledger.RecordCreated();
    }

    public void Dispose() => _ledger.RecordDisposed();
}

/// <summary>Stress page whose async lifecycle yields, so concurrent
/// callers interleave inside the pump instead of marching in lock-step.</summary>
public abstract class StressPageBase : NavigationViewModelBase, INavigationAwareAsync
{
    protected StressPageBase(PageScopeMarker marker) => _ = marker;

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default) =>
        await Task.Yield();

    public async Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) =>
        await Task.Yield();
}

public sealed class StressPageA : StressPageBase
{
    public StressPageA(PageScopeMarker marker) : base(marker) { }
}

public sealed class StressPageB : StressPageBase
{
    public StressPageB(PageScopeMarker marker) : base(marker) { }
}

public sealed class StressPageC : StressPageBase
{
    public StressPageC(PageScopeMarker marker) : base(marker) { }
}

/// <summary>Stress page whose guard vetoes departures toward a target
/// whose parameter is the string "veto", exercising guard-denied paths
/// under concurrency.</summary>
public sealed class StressGuardedPage : StressPageBase, INavigationGuardWithContext
{
    public StressGuardedPage(PageScopeMarker marker) : base(marker) { }

    public Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken) =>
        Task.FromResult(!Equals(context.Parameter, "veto"));
}

public class ConcurrencyStressTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<PageScopeLedger>();
        services.AddScoped<PageScopeMarker>();
        services.AddTransient<StressPageA>();
        services.AddTransient<StressPageB>();
        services.AddTransient<StressPageC>();
        services.AddTransient<StressGuardedPage>();
        return services.BuildServiceProvider();
    }

    private static async Task<bool> NavigateRandomAsync(INavigationService nav, Random rng, string region)
    {
        var retention = rng.Next(3) == 0 ? NavigationRetention.KeepAlive : NavigationRetention.Destroy;
        var operation = rng.Next(10) switch
        {
            0 => NavigationOperation.Replace,
            1 => NavigationOperation.Reset,
            _ => NavigationOperation.Push,
        };
        object? parameter = rng.Next(6) == 0 ? "veto" : rng.Next(1000);

        return rng.Next(4) switch
        {
            0 => await nav.NavigateToAsync<StressPageA, object>(parameter!, regionName: region, operation: operation, retention: retention),
            1 => await nav.NavigateToAsync<StressPageB, object>(parameter!, regionName: region, operation: operation, retention: retention),
            2 => await nav.NavigateToAsync<StressPageC, object>(parameter!, regionName: region, operation: operation, retention: retention),
            _ => await nav.NavigateToAsync<StressGuardedPage, object>(parameter!, regionName: region, operation: operation, retention: retention),
        };
    }

    private static async Task WaitForScopeBalanceAsync(PageScopeLedger ledger, string because)
    {
        // Scope disposal for settled state is synchronous with the
        // operations, but abandoned background work may settle a moment
        // later; poll briefly instead of asserting on a knife's edge.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var now = ledger.Snapshot();
            if (now.Created == now.Disposed)
            {
                return;
            }

            await Task.Delay(50);
        }

        var end = ledger.Snapshot();
        Assert.Fail($"Page scopes leaked ({because}): created {end.Created}, disposed {end.Disposed}.");
    }

    /// <summary>
    /// Concurrency storm: many workers navigate, go back, and clear
    /// caches across two regions at once. Every operation must complete
    /// (no hangs, no unexpected exceptions, no error reports), the
    /// service must stay usable afterwards, and once it is disposed
    /// every page scope it ever created must have been disposed.
    /// </summary>
    [Fact]
    public async Task ConcurrentStorm_CompletesCleanly_AndDisposesEveryScope()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();

        var subscriberErrors = new List<Exception>();
        using var subscription = nav.SubscribeSubscriberError(ex =>
        {
            lock (subscriberErrors)
            {
                subscriberErrors.Add(ex);
            }
        });

        string[] regions = ["StormA", "StormB"];
        var failures = new List<Exception>();
        var successCount = 0;

        async Task Worker(int seed)
        {
            var rng = new Random(seed);
            try
            {
                for (var i = 0; i < 120; i++)
                {
                    var region = regions[rng.Next(regions.Length)];
                    var succeeded = false;
                    switch (rng.Next(10))
                    {
                        case 0:
                        case 1:
                            succeeded = await nav.GoBackAsync(region);
                            break;
                        case 2:
                            nav.ClearCache(region);
                            break;
                        case 3:
                            await nav.ClearCacheAsync(region);
                            break;
                        default:
                            succeeded = await NavigateRandomAsync(nav, rng, region);
                            break;
                    }

                    if (succeeded)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            }
        }

        var workers = Enumerable.Range(0, 8).Select(seed => Task.Run(() => Worker(seed * 7919 + 13))).ToArray();
        var all = Task.WhenAll(workers);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(120)));
        Assert.True(ReferenceEquals(finished, all), "The concurrency storm did not finish within 120 seconds (hang suspected).");
        await all;

        Assert.Empty(failures);
        lock (subscriberErrors)
        {
            Assert.Empty(subscriberErrors);
        }

        // A contention-only regression (intents under concurrent
        // registration systematically completing false, being dropped,
        // or never landing) produces no exception and no scope leak —
        // only this tally sees it. Supersession legitimately completes
        // most concurrent calls false, so the floor sits far below the
        // counts observed in practice (dozens of executed hops).
        Assert.True(
            Volatile.Read(ref successCount) >= 15,
            $"Only {Volatile.Read(ref successCount)} of ~960 storm operations succeeded; the pump appears to drop work under contention.");

        // The service is still usable after the storm.
        Assert.True(await nav.NavigateToAsync<StressPageA>("StormA"));
        Assert.IsType<StressPageA>(nav.GetCurrentViewModel("StormA"));

        await ((IAsyncDisposable)nav).DisposeAsync();
        await WaitForScopeBalanceAsync(ledger, "after the storm and service disposal");
    }

    /// <summary>
    /// Seeded random walk (single-threaded, deterministic): after every
    /// operation the region's observable state must be self-consistent,
    /// and the last RegionNavigated event must name the page that is
    /// actually current (the event is raised before the landing
    /// intent completes, and each step fully settles before the next).
    /// </summary>
    [Fact]
    public async Task SeededRandomWalk_RegionStateStaysSelfConsistent()
    {
        var provider = BuildProvider();
        var ledger = provider.GetRequiredService<PageScopeLedger>();
        var nav = provider.GetRequiredService<INavigationService>();

        string[] regions = ["WalkA", "WalkB"];
        var lastEvent = new Dictionary<string, INavigationViewModel?>();
        nav.RegionNavigated += (region, vm) => lastEvent[region] = vm;

        var rng = new Random(20261023);
        for (var i = 0; i < 300; i++)
        {
            var region = regions[rng.Next(regions.Length)];
            if (rng.Next(10) < 3)
            {
                await nav.GoBackAsync(region);
            }
            else if (rng.Next(20) == 0)
            {
                nav.ClearCache(region);
            }
            else
            {
                await NavigateRandomAsync(nav, rng, region);
            }

            var current = nav.GetCurrentViewModel(region);
            Assert.True(
                (current is null) == (nav.GetCurrentRetention(region) is null),
                $"Step {i}: current ViewModel and retention disagree in region {region}.");
            Assert.True(
                !nav.CanGoBack(region) || current is not null,
                $"Step {i}: CanGoBack is true for an empty region {region}.");
            if (current is not null && lastEvent.TryGetValue(region, out var landed))
            {
                Assert.Same(landed, current);
            }
        }

        await ((IAsyncDisposable)nav).DisposeAsync();
        await WaitForScopeBalanceAsync(ledger, "after the random walk and service disposal");
    }
}
