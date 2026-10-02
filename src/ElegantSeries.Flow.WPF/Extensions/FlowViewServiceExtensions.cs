using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ElegantSeries.Flow.WPF.Locating;

namespace ElegantSeries.Flow.WPF.Extensions;

/// <summary>
/// Dependency-injection helpers for the ElegantSeries.Flow WPF integration.
/// </summary>
public static class FlowViewServiceExtensions
{
    /// <summary>
    /// Registers the WPF view infrastructure: a singleton <see cref="IViewLocator"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional view registrations, applied once when the locator is first resolved.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Without <paramref name="configure"/>, registration uses
    /// <c>TryAddSingleton</c> and never overrides an existing
    /// <see cref="IViewLocator"/> registration. With <paramref name="configure"/>,
    /// an explicit registration is added; per Microsoft DI rules the last
    /// registration wins when <see cref="IViewLocator"/> is resolved, so calling
    /// this method twice with a configuration replaces the earlier one.
    /// </para>
    /// <para>
    /// The <paramref name="configure"/> action runs lazily inside the singleton
    /// factory, i.e. once, on first resolution of <see cref="IViewLocator"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFlowViews(
        this IServiceCollection services,
        Action<IViewLocator>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
        {
            services.TryAddSingleton<IViewLocator, ViewLocator>();
        }
        else
        {
            services.AddSingleton<IViewLocator>(_ =>
            {
                var locator = new ViewLocator();
                configure(locator);
                return locator;
            });
        }

        return services;
    }
}
