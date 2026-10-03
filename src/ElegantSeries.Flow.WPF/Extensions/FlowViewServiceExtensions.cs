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
    /// Optional view registrations, applied immediately when this method is called.
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
    /// The <paramref name="configure"/> action runs eagerly, i.e. during this
    /// call rather than on first resolution of <see cref="IViewLocator"/>.
    /// This is required so that ViewModel lifetime registrations
    /// (<see cref="IViewLocator.RegisterTransient{TView, TViewModel}"/>,
    /// <see cref="IViewLocator.RegisterSingleton{TView, TViewModel}"/>) take
    /// effect before the service provider is built; registrations added after
    /// <c>BuildServiceProvider()</c> would be silently ignored. As a side
    /// benefit, configuration errors fail fast at startup.
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
            var locator = new ViewLocator(services);
            configure(locator);
            services.AddSingleton<IViewLocator>(locator);
        }

        return services;
    }
}
