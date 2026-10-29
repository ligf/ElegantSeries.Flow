using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElegantSeries.Flow.Diagnostics;

/// <summary>
/// Registration helpers for the <c>ElegantSeries.Flow.Diagnostics</c>
/// package.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="NavigationDiagnostics"/> with the same
    /// lifetime as the <see cref="INavigationService"/> registration it
    /// observes: a singleton observer for the default singleton
    /// navigation service, a scoped observer per window scope when the
    /// navigation service is registered scoped. Mirroring matters — a
    /// singleton observer over a scoped navigation service would fail
    /// scope validation or silently observe the root scope's unused
    /// instance. The observer subscribes when it is first resolved,
    /// counts from that moment, and is disposed by the container with
    /// its provider or scope. The observer's own registration uses
    /// TryAdd: if an observer is already registered, this call leaves
    /// it in place.
    /// </summary>
    /// <remarks>
    /// Call <c>AddElegantSeriesFlow</c> first: the navigation service
    /// must already be registered so its lifetime can be mirrored.
    /// Calling this method before any <see cref="INavigationService"/>
    /// registration exists throws <see cref="InvalidOperationException"/>
    /// instead of silently defaulting to a singleton, and a transient
    /// navigation service registration is rejected the same way (an
    /// observer over a per-resolution navigation service would count
    /// nothing meaningful).
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddElegantSeriesFlowDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Unkeyed resolution — the kind the observer's constructor
        // performs — ignores keyed registrations entirely and uses the
        // LAST unkeyed registration for a service type, so the last
        // unkeyed INavigationService descriptor is the one whose
        // lifetime the observer must mirror.
        ServiceLifetime? lifetime = null;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(INavigationService) && !descriptor.IsKeyedService)
                lifetime = descriptor.Lifetime;
        }

        if (lifetime is null)
            throw new InvalidOperationException(
                "AddElegantSeriesFlowDiagnostics requires the navigation service to be registered first: " +
                "call AddElegantSeriesFlow before AddElegantSeriesFlowDiagnostics, so the observer can " +
                "mirror the navigation service's lifetime.");

        if (lifetime == ServiceLifetime.Transient)
            throw new InvalidOperationException(
                "AddElegantSeriesFlowDiagnostics cannot observe a transient INavigationService: " +
                "each resolution would create an isolated navigation service, and the observer would " +
                "subscribe to an instance nobody navigates with. Register the navigation service as " +
                "Singleton or Scoped.");

        services.TryAdd(new ServiceDescriptor(
            typeof(NavigationDiagnostics), typeof(NavigationDiagnostics), lifetime.Value));
        return services;
    }
}
