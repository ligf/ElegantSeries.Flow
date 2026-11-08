using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>
/// Contract tests for <see cref="INavigationService.GetAllNavigationStacks"/>
/// (design doc <c>p2-2-atomic-snapshot-design.md</c> §3): every region's
/// stack is read under one lock acquisition, so the picture is a state the
/// service occupied at a single instant — where per-region reads could
/// stitch stacks from different moments into a combination that never
/// existed.
/// </summary>
public class AllNavigationStacksTests
{
    private const string Side = "SideRegion";

    [Fact]
    public async Task ReturnsEveryRegionBottomToTop()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await nav.NavigateToAsync<Pair1ViewModel>());
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>(Side));

        var all = nav.GetAllNavigationStacks();
        Assert.Equal(2, all.Count);

        var main = all[NavigationDefaults.DefaultRegionName];
        Assert.Equal(2, main.Count);
        Assert.IsType<TestHomeViewModel>(main[0]);
        Assert.IsType<Pair1ViewModel>(main[1]);

        // The atomic read agrees with the per-region query on content.
        Assert.Equal(nav.GetNavigationStack(), main);

        var side = Assert.Single(all[Side]);
        Assert.IsType<TestHomeViewModel>(side);
    }

    [Fact]
    public async Task ReturnedPictureIsDetachedFromLaterNavigation()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        var before = nav.GetAllNavigationStacks();

        Assert.True(await nav.NavigateToAsync<Pair1ViewModel>());

        // The earlier picture is a copy: the later navigation is not in it.
        Assert.Single(before[NavigationDefaults.DefaultRegionName]);

        var after = nav.GetAllNavigationStacks();
        Assert.Equal(2, after[NavigationDefaults.DefaultRegionName].Count);
        Assert.NotSame(before, after);
        Assert.NotSame(
            before[NavigationDefaults.DefaultRegionName],
            after[NavigationDefaults.DefaultRegionName]);
    }

    [Fact]
    public async Task AfterDisposal_ReturnsEmptyAndDoesNotThrow()
    {
        var provider = BuildProvider();
        var nav = provider.GetRequiredService<INavigationService>();
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());

        // Disposing the provider disposes the singleton service.
        provider.Dispose();

        var all = nav.GetAllNavigationStacks();
        Assert.Empty(all);
    }

    [Fact]
    public async Task ConcurrentPairedNavigation_EveryReadIsAStateThatExisted()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();

        // Permanent roots: both regions start at depth 1 and the writer's
        // pops never go below them.
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>(Side));

        Func<string, Task<bool>>[] pushes =
        [
            region => nav.NavigateToAsync<Pair1ViewModel>(region),
            region => nav.NavigateToAsync<Pair2ViewModel>(region),
            region => nav.NavigateToAsync<Pair3ViewModel>(region),
            region => nav.NavigateToAsync<Pair4ViewModel>(region),
            region => nav.NavigateToAsync<Pair5ViewModel>(region),
            region => nav.NavigateToAsync<Pair6ViewModel>(region),
        ];
        var main = NavigationDefaults.DefaultRegionName;

        var writer = Task.Run(async () =>
        {
            for (var cycle = 0; cycle < 60; cycle++)
            {
                // Grow Main first, then Side: depths pass through
                // (k, k) and (k+1, k) only.
                foreach (var push in pushes)
                {
                    Assert.True(await push(main));
                    Assert.True(await push(Side));
                }

                // Shrink Side first, then Main: depths pass through
                // (k, k) and (k, k-1) only. So at every real instant
                // side <= main <= side + 1.
                for (var i = 0; i < pushes.Length; i++)
                {
                    Assert.True(await nav.GoBackAsync(Side));
                    Assert.True(await nav.GoBackAsync(main));
                }
            }

        });

        // The loop keys on the writer TASK, not a completion flag: if a
        // navigation inside the writer faults, the loop still exits and
        // the await below surfaces that failure — a flag set only at the
        // end of the writer would spin here forever and hang the suite
        // instead of reporting the real error.
        long reads = 0;
        while (!writer.IsCompleted)
        {
            var all = nav.GetAllNavigationStacks();
            var mainDepth = all[main].Count;
            var sideDepth = all[Side].Count;
            Assert.True(
                sideDepth <= mainDepth && mainDepth <= sideDepth + 1,
                $"picture that never existed: Main depth {mainDepth}, Side depth {sideDepth}");
            reads++;
        }

        await writer;
        Assert.True(reads > 50, $"reader barely ran ({reads} reads)");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<Pair1ViewModel>();
        services.AddTransient<Pair2ViewModel>();
        services.AddTransient<Pair3ViewModel>();
        services.AddTransient<Pair4ViewModel>();
        services.AddTransient<Pair5ViewModel>();
        services.AddTransient<Pair6ViewModel>();
        return services.BuildServiceProvider();
    }

    private sealed class Pair1ViewModel : NavigationViewModelBase;
    private sealed class Pair2ViewModel : NavigationViewModelBase;
    private sealed class Pair3ViewModel : NavigationViewModelBase;
    private sealed class Pair4ViewModel : NavigationViewModelBase;
    private sealed class Pair5ViewModel : NavigationViewModelBase;
    private sealed class Pair6ViewModel : NavigationViewModelBase;
}
