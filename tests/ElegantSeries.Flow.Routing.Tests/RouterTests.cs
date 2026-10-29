using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Routing.Tests;

/// <summary>
/// Contract tests for the routing package: every test maps to a
/// section of the contract in <c>files/routing-design.md</c>. A real
/// <see cref="INavigationService"/> is used throughout, so a matched
/// route's effect is observed on the actual navigation stack.
/// </summary>
public class RouterTests
{
    // ── Matching & completion semantics ─────────────────────────────

    [Fact]
    public async Task LiteralAndPlaceholder_Match_FactoryReceivesValuesAndQuery()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        RouteMatch? seen = null;
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("home")
            .Map<DetailViewModel, DetailParam>("orders/{id}", match =>
            {
                seen = match;
                return new DetailParam(int.Parse(match.Values["id"]), match.Query["expand"]);
            })
            .Build());

        Assert.True(await router.NavigateAsync("home"));
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel());

        Assert.True(await router.NavigateAsync("orders/42?expand=items"));
        var detail = Assert.IsType<DetailViewModel>(nav.GetCurrentViewModel());
        Assert.Equal(new DetailParam(42, "items"), detail.Received);

        Assert.NotNull(seen);
        Assert.Equal("orders/42?expand=items", seen.RawUri);
        Assert.Equal(["orders", "42"], seen.Segments);
        Assert.Equal("42", seen.Values["id"]);
        Assert.Equal("items", seen.Query["expand"]);
    }

    [Fact]
    public async Task LiteralSegments_MatchCaseInsensitively()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("HoMe")
            .Build());

        Assert.True(await router.NavigateAsync("hOmE"));
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task OverlappingPatterns_FirstRegisteredWins()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("pages/{name}")
            .Map<DetailViewModel, DetailParam>("pages/{id}",
                match => new DetailParam(0, null))
            .Build());

        Assert.True(await router.NavigateAsync("pages/x"));
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task RootRoute_MatchesEmptyAndSlashUris()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("")
            .Map<DetailViewModel, DetailParam>("d", _ => new DetailParam(0, null))
            .Build());

        Assert.True(await router.NavigateAsync(""));
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel());
        Assert.True(await router.NavigateAsync("d"));
        Assert.IsType<DetailViewModel>(nav.GetCurrentViewModel());
        Assert.True(await router.NavigateAsync("/"));
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task FullUri_SchemePrefixStripped_FragmentIgnored()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        string? name = null;
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel, string>("users/{name}", match => name = match.Values["name"])
            .Build());

        Assert.True(await router.NavigateAsync("app://users/bob#profile"));
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel());
        Assert.Equal("bob", name);
    }

    [Fact]
    public async Task SegmentsAndQuery_ArePercentDecoded_PlusIsSpaceOnlyInQuery()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        RouteMatch? seen = null;
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel, string>("files/{name}", match =>
            {
                seen = match;
                return match.Values["name"];
            })
            .Build());

        Assert.True(await router.NavigateAsync("files/a%20b+c?q=x+y%2Bz"));
        Assert.NotNull(seen);
        // Path: %20 decodes to a space, a literal + stays a +.
        Assert.Equal("a b+c", seen.Values["name"]);
        // Query: + decodes to a space, %2B stays a +.
        Assert.Equal("x y+z", seen.Query["q"]);
    }

    [Fact]
    public async Task RepeatedQueryKey_LastValueWins_KeyWithoutValueIsEmpty()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        RouteMatch? seen = null;
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel, string>("h", match =>
            {
                seen = match;
                return "";
            })
            .Build());

        Assert.True(await router.NavigateAsync("h?k=1&k=2&flag"));
        Assert.NotNull(seen);
        Assert.Equal("2", seen.Query["k"]);
        Assert.Equal("", seen.Query["flag"]);
    }

    [Fact]
    public async Task NoMatch_NavigateThrows_TryReturnsFalse()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("home")
            .Build());

        var ex = await Assert.ThrowsAsync<RouteNotFoundException>(
            () => router.NavigateAsync("missing/thing"));
        Assert.Equal("missing/thing", ex.Uri);
        Assert.False(await router.TryNavigateAsync("missing/thing"));
        Assert.Null(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task GuardVeto_FateFalse_PassesThrough()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<GuardedViewModel>("guarded")
            .Map<HomeViewModel>("home")
            .Build());

        Assert.True(await router.NavigateAsync("guarded"));
        // GuardedViewModel's guard vetoes departure: fate false, and
        // the current page does not change.
        Assert.False(await router.NavigateAsync("home"));
        Assert.IsType<GuardedViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task Route_RegionAndOperation_AreHonored()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("a", region: "Second")
            .Map<DetailViewModel, DetailParam>("b",
                _ => new DetailParam(1, null),
                region: "Second", operation: NavigationOperation.Replace)
            .Build());

        Assert.True(await router.NavigateAsync("a"));
        Assert.IsType<HomeViewModel>(nav.GetCurrentViewModel("Second"));
        Assert.Null(nav.GetCurrentViewModel());

        Assert.True(await router.NavigateAsync("b"));
        Assert.IsType<DetailViewModel>(nav.GetCurrentViewModel("Second"));
        // Replace: the stack still holds exactly one page.
        Assert.False(nav.CanGoBack("Second"));
    }

    [Fact]
    public async Task SchemeDelimiter_MidUri_IsNotStripped()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("{only}")
            .Build());

        // If a "://" anywhere were treated as a scheme delimiter,
        // "x/y://z" would be truncated to "z" and match "{only}".
        Assert.False(await router.TryNavigateAsync("x/y://z"));
        Assert.Null(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task NonAsciiSchemeCandidate_IsNotStripped()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("{only}")
            .Build());

        // A scheme is ASCII (RFC 3986): "café" is not one, so nothing
        // is stripped and the two-segment text cannot match "{only}".
        Assert.False(await router.TryNavigateAsync("café://x"));
        Assert.Null(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task CancelledToken_MatchingRoute_ThrowsOperationCanceled()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("home")
            .Build());

        // Matching does not consult the token; the navigation call
        // observes it and Core's cancellation surfaces unchanged.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.NavigateAsync("home", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ConcurrentNavigations_CompleteAndLandOnARoutedPage()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel>("home")
            .Map<DetailViewModel, DetailParam>("detail", _ => new DetailParam(1, null))
            .Build());

        var calls = Enumerable.Range(0, 8)
            .Select(i => router.NavigateAsync(i % 2 == 0 ? "home" : "detail"))
            .ToArray();
        var fates = await Task.WhenAll(calls);

        // Superseded requests legitimately report false; the contract
        // is only that concurrent calls are safe and at least the
        // surviving request executed.
        Assert.Contains(true, fates);
        Assert.True(nav.GetCurrentViewModel() is HomeViewModel or DetailViewModel);
    }

    [Fact]
    public async Task FactoryReturningNull_PassesThroughAsNullParameter()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<DetailViewModel, DetailParam>("nullable", _ => null!)
            .Build());

        Assert.True(await router.NavigateAsync("nullable"));
        var detail = Assert.IsType<DetailViewModel>(nav.GetCurrentViewModel());
        Assert.Null(detail.Received);
    }

    [Fact]
    public async Task TryNavigate_GuardVeto_ReturnsFalse()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<GuardedViewModel>("guarded")
            .Map<HomeViewModel>("home")
            .Build());

        Assert.True(await router.TryNavigateAsync("guarded"));
        // A vetoed fate is false through TryNavigateAsync as well —
        // indistinguishable from no-match by design.
        Assert.False(await router.TryNavigateAsync("home"));
        Assert.IsType<GuardedViewModel>(nav.GetCurrentViewModel());
    }

    // ── Failure semantics ───────────────────────────────────────────

    [Fact]
    public async Task FactoryException_PropagatesUnwrapped()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder()
            .Map<HomeViewModel, string>("boom", _ => throw new InvalidOperationException("factory blew up"))
            .Build());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.NavigateAsync("boom"));
        Assert.Equal("factory blew up", ex.Message);
        Assert.Null(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task UriValidation_NullAndWhitespace()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var router = new Router(nav, new RouteTableBuilder().Map<HomeViewModel>("h").Build());

        await Assert.ThrowsAsync<ArgumentNullException>(() => router.NavigateAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => router.NavigateAsync("   "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => router.TryNavigateAsync(null!));
    }

    [Theory]
    [InlineData("orders/{id")]      // unbalanced
    [InlineData("orders/id}")]      // unbalanced
    [InlineData("orders/{}")]       // empty name
    [InlineData("orders/{9id}")]    // illegal first char
    [InlineData("orders/{i-d}")]    // illegal char
    [InlineData("orders/x{id}")]    // placeholder not whole-segment
    [InlineData("orders/{id}/x/{id}")] // duplicated name
    [InlineData("   ")]             // whitespace pattern
    public void MalformedPattern_ThrowsAtMapTime(string pattern)
    {
        Assert.Throws<ArgumentException>(() =>
            new RouteTableBuilder().Map<HomeViewModel>(pattern));
    }

    [Fact]
    public void NullArguments_ThrowAtMapTime()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RouteTableBuilder().Map<HomeViewModel>(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new RouteTableBuilder().Map<HomeViewModel, string>("ok", null!));
    }

    // ── Builder / table behaviour ───────────────────────────────────

    [Fact]
    public void Build_Snapshots_LaterRegistrationsDoNotAffectBuiltTable()
    {
        var builder = new RouteTableBuilder().Map<HomeViewModel>("a");
        var table = builder.Build();
        builder.Map<HomeViewModel>("b");

        Assert.Equal(1, table.Count);
        Assert.Equal(2, builder.Build().Count);
    }

    [Fact]
    public void RouterConstructor_NullArguments_Throw()
    {
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var table = new RouteTableBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => new Router(null!, table));
        Assert.Throws<ArgumentNullException>(() => new Router(nav, null!));
    }

    // ── DI registration ─────────────────────────────────────────────

    [Fact]
    public async Task Di_SingletonRouter_ResolvesAndNavigates()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowRouting(routes => routes.Map<HomeViewModel>("home"));
        using var provider = services.BuildServiceProvider();

        var router = provider.GetRequiredService<IRouter>();
        Assert.Same(router, provider.GetRequiredService<IRouter>());
        Assert.True(await router.NavigateAsync("home"));
        Assert.IsType<HomeViewModel>(
            provider.GetRequiredService<INavigationService>().GetCurrentViewModel());
        Assert.Equal(1, provider.GetRequiredService<RouteTable>().Count);
    }

    [Fact]
    public void Di_ScopedNavigation_RootRouterResolutionFails_ScopeWorks()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(ServiceLifetime.Scoped);
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowRouting(routes => routes.Map<HomeViewModel>("home"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        Assert.Throws<InvalidOperationException>(() => provider.GetService<IRouter>());

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var router1 = scope1.ServiceProvider.GetRequiredService<IRouter>();
        Assert.Same(router1, scope1.ServiceProvider.GetRequiredService<IRouter>());
        Assert.NotSame(router1, scope2.ServiceProvider.GetRequiredService<IRouter>());
    }

    [Fact]
    public void Di_WithoutNavigationService_Throws()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddElegantSeriesFlowRouting(routes => routes.Map<HomeViewModel>("home")));
        Assert.Contains("AddElegantSeriesFlow", ex.Message);
    }

    [Fact]
    public void Di_WithTransientNavigationService_Throws()
    {
        var services = new ServiceCollection();
        services.AddTransient<INavigationService, NavigationService>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddElegantSeriesFlowRouting(routes => routes.Map<HomeViewModel>("home")));
        Assert.Contains("transient", ex.Message);
    }

    [Fact]
    public void Di_KeyedNavigationService_IsIgnoredByLifetimeMirroring()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        // A keyed registration after the unkeyed one must not change
        // what the helper mirrors: unkeyed resolution (the router's
        // own) never sees it. Without the keyed filter, the trailing
        // keyed Transient descriptor would make the helper throw.
        services.AddKeyedTransient<INavigationService, NavigationService>("audit");
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowRouting(routes => routes.Map<HomeViewModel>("home"));
        using var provider = services.BuildServiceProvider();

        var router = provider.GetRequiredService<IRouter>();
        Assert.Same(router, provider.GetRequiredService<IRouter>());
    }

    [Fact]
    public void Di_CalledTwice_KeepsFirstRouterAndTable()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowRouting(routes => routes.Map<HomeViewModel>("first"));
        services.AddElegantSeriesFlowRouting(routes => routes.Map<HomeViewModel>("second"));
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IRouter));
        Assert.Single(services, d => d.ServiceType == typeof(RouteTable));
        Assert.Equal(1, provider.GetRequiredService<RouteTable>().Count);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<DetailViewModel>();
        services.AddTransient<GuardedViewModel>();
        return services.BuildServiceProvider();
    }

    public sealed record DetailParam(int Id, string? Expand);

    private sealed class HomeViewModel : NavigationViewModelBase, INavigationAware<string>
    {
        public void OnNavigatedTo(string parameter)
        {
        }

        public void OnNavigatedFrom()
        {
        }
    }

    private sealed class DetailViewModel : NavigationViewModelBase, INavigationAware<DetailParam>
    {
        public DetailParam? Received { get; private set; }

        public void OnNavigatedTo(DetailParam parameter) => Received = parameter;

        public void OnNavigatedFrom()
        {
        }
    }

    private sealed class GuardedViewModel : NavigationViewModelBase, INavigationGuard
    {
        public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

}
