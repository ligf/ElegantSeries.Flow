using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElegantSeries.Flow.Routing;

/// <summary>
/// Registration helpers for the <c>ElegantSeries.Flow.Routing</c>
/// package.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Builds a route table from <paramref name="configure"/> and
    /// registers <see cref="IRouter"/> with the same lifetime as the
    /// <see cref="INavigationService"/> registration it navigates
    /// through: a singleton router for the default singleton
    /// navigation service, a scoped router per window scope when the
    /// navigation service is registered scoped. Mirroring matters — a
    /// singleton router over a scoped navigation service would fail
    /// scope validation or silently capture the root scope's unused
    /// instance. The route table itself is registered as a singleton
    /// instance (it is immutable). Both registrations use TryAdd: if
    /// a router or a route table is already registered, this call
    /// leaves it in place (and its table is not replaced).
    /// </summary>
    /// <remarks>
    /// Call <c>AddElegantSeriesFlow</c> first: the navigation service
    /// must already be registered so its lifetime can be mirrored.
    /// Calling this method before any <see cref="INavigationService"/>
    /// registration exists throws <see cref="InvalidOperationException"/>
    /// instead of silently defaulting to a singleton, and a transient
    /// navigation service registration is rejected the same way (a
    /// router over a per-resolution navigation service would navigate
    /// an instance nobody else sees).
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Registers the routes on a
    /// <see cref="RouteTableBuilder"/>; runs synchronously during this
    /// call.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddElegantSeriesFlowRouting(
        this IServiceCollection services, Action<RouteTableBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Unkeyed resolution — the kind the router's constructor
        // performs — ignores keyed registrations entirely and uses the
        // LAST unkeyed registration for a service type, so the last
        // unkeyed INavigationService descriptor is the one whose
        // lifetime the router must mirror.
        ServiceLifetime? lifetime = null;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(INavigationService) && !descriptor.IsKeyedService)
                lifetime = descriptor.Lifetime;
        }

        if (lifetime is null)
            throw new InvalidOperationException(
                "AddElegantSeriesFlowRouting requires the navigation service to be registered first: " +
                "call AddElegantSeriesFlow before AddElegantSeriesFlowRouting, so the router can " +
                "mirror the navigation service's lifetime.");

        if (lifetime == ServiceLifetime.Transient)
            throw new InvalidOperationException(
                "AddElegantSeriesFlowRouting cannot navigate through a transient INavigationService: " +
                "each resolution would create an isolated navigation service, and the router would " +
                "navigate an instance nobody else sees. Register the navigation service as " +
                "Singleton or Scoped.");

        var builder = new RouteTableBuilder();
        configure(builder);
        var routes = builder.Build();

        services.TryAdd(new ServiceDescriptor(typeof(RouteTable), routes));
        services.TryAdd(new ServiceDescriptor(typeof(IRouter), typeof(Router), lifetime.Value));
        return services;
    }
}
