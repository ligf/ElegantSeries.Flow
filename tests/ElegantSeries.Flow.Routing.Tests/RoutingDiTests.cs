using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElegantSeries.Flow.Routing.Tests;

/// <summary>
/// The <c>AddElegantSeriesFlowRouting</c> lifetime-mirror contract —
/// the same shape the Diagnostics package pins for its own helper:
/// the router mirrors the LAST unkeyed
/// <see cref="INavigationService"/> descriptor, keyed registrations
/// are invisible to the mirror, a transient effective registration
/// is rejected, and the route table and router registrations
/// themselves are TryAdd (a pre-registration wins).
/// Only the rows the existing DI section in <c>RouterTests</c>
/// does not already pin live here (last-unkeyed in both orders,
/// last-unkeyed transient, keyed-only, pre-registered table/router);
/// the no-registration, keyed-after-unkeyed, called-twice and
/// per-scope/shared resolution rows stay in that section.
/// </summary>
public sealed class RoutingDiTests
{
    private sealed class DiHomeViewModel : NavigationViewModelBase;

    private sealed class StubRouter : IRouter
    {
        public Task<bool> NavigateAsync(string uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> TryNavigateAsync(string uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private static void Configure(RouteTableBuilder routes) => routes.Map<DiHomeViewModel>("home");

    [Fact]
    public void Registration_OnlyKeyedNavigationService_Throws()
    {
        // Only a keyed registration exists: there is no unkeyed
        // INavigationService for the router to consume, so the helper
        // fails fast like the no-registration case.
        var services = new ServiceCollection();
        services.AddKeyedSingleton<INavigationService, NavigationService>("main");
        Assert.Throws<InvalidOperationException>(() => services.AddElegantSeriesFlowRouting(Configure));
    }

    [Fact]
    public void Registration_MultipleUnkeyedRegistrations_MirrorsTheLastOne()
    {
        // Unkeyed single-service resolution — the kind the router's
        // constructor performs — uses the LAST unkeyed registration,
        // so the mirror must take the last unkeyed descriptor's
        // lifetime, not the first one it encounters.
        var scopedLast = new ServiceCollection();
        scopedLast.AddSingleton<INavigationService, NavigationService>();
        scopedLast.AddScoped<INavigationService, NavigationService>();
        scopedLast.AddElegantSeriesFlowRouting(Configure);
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(scopedLast, d => d.ServiceType == typeof(IRouter)).Lifetime);

        var singletonLast = new ServiceCollection();
        singletonLast.AddScoped<INavigationService, NavigationService>();
        singletonLast.AddSingleton<INavigationService, NavigationService>();
        singletonLast.AddElegantSeriesFlowRouting(Configure);
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(singletonLast, d => d.ServiceType == typeof(IRouter)).Lifetime);
    }

    [Fact]
    public void Registration_LastUnkeyedRegistrationTransient_Throws()
    {
        // The transient rejection also follows the last unkeyed
        // descriptor: an earlier singleton registration does not
        // rescue a collection whose effective registration is
        // transient.
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddTransient<INavigationService, NavigationService>();
        Assert.Throws<InvalidOperationException>(() => services.AddElegantSeriesFlowRouting(Configure));
    }

    [Fact]
    public void Registration_PreRegisteredTableAndRouter_AreLeftInPlace()
    {
        // Both registrations are TryAdd: a route table or router the
        // caller registered first stays, and the helper's own table
        // (built from configure) is discarded.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        var prebuiltTable = new RouteTableBuilder().Map<DiHomeViewModel>("prebuilt").Build();
        var stub = new StubRouter();
        services.AddSingleton(prebuiltTable);
        services.AddSingleton<IRouter>(stub);
        services.AddElegantSeriesFlowRouting(Configure);

        Assert.Single(services, d => d.ServiceType == typeof(IRouter));
        Assert.Single(services, d => d.ServiceType == typeof(RouteTable));

        using var provider = services.BuildServiceProvider();
        Assert.Same(prebuiltTable, provider.GetRequiredService<RouteTable>());
        Assert.Same(stub, provider.GetRequiredService<IRouter>());
    }

    [Fact]
    public void Registration_PreRegisteredTableOnly_StillRegistersTheRouter()
    {
        // A pre-built table registered on its own (Build is public)
        // is only half of what this method registers: the call is not
        // a no-op, the router half still lands through TryAdd, and it
        // binds to the caller's table — the helper's own built table
        // is the one discarded.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        var prebuiltTable = new RouteTableBuilder().Map<DiHomeViewModel>("prebuilt").Build();
        services.AddSingleton(prebuiltTable);
        services.AddElegantSeriesFlowRouting(Configure);

        Assert.Single(services, d => d.ServiceType == typeof(IRouter));
        Assert.Single(services, d => d.ServiceType == typeof(RouteTable));

        using var provider = services.BuildServiceProvider();
        Assert.Same(prebuiltTable, provider.GetRequiredService<RouteTable>());
        Assert.IsType<Router>(provider.GetRequiredService<IRouter>());
    }

}
