using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ElegantSeries.Flow.Avalonia.Locating;

namespace ElegantSeries.Flow.Avalonia.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering ElegantSeries.Flow's
/// Avalonia view infrastructure.
/// </summary>
public static class FlowViewServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="IViewLocator"/> singleton used by
    /// <see cref="Hosting.NavigationHost"/> to resolve views for ViewModels.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional startup configuration that registers view/view-model pairs on the
    /// locator. Runs immediately, when this method is called.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Without <paramref name="configure"/>, registration uses
    /// <c>TryAddSingleton</c> and never overrides an existing
    /// <see cref="IViewLocator"/> registration. With <paramref name="configure"/>,
    /// an explicit registration is added; per Microsoft DI rules the last
    /// registration wins when <see cref="IViewLocator"/> is resolved, so the
    /// view <i>mappings</i> of a later call replace those of an earlier one.
    /// ViewModel lifetime registrations, however, accumulate in call order
    /// and are not rolled back by a later call.
    /// </para>
    /// <para>
    /// The <paramref name="configure"/> action runs eagerly, i.e. during this
    /// call rather than on first resolution of <see cref="IViewLocator"/>.
    /// This is required so that ViewModel lifetime registrations
    /// (<see cref="Locating.IViewLocator.RegisterTransient{TView, TViewModel}"/>,
    /// <see cref="Locating.IViewLocator.RegisterSingleton{TView, TViewModel}"/>)
    /// take effect before the service provider is built; registrations added
    /// after <c>BuildServiceProvider()</c> would be silently ignored. As a side
    /// benefit, configuration errors fail fast at startup.
    /// </para>
    /// <para>
    /// The navigation service itself (<c>ElegantSeries.Flow.Core</c>) is registered
    /// separately; this method only covers the Avalonia view layer.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddFlowViews(locator =>
    /// {
    ///     locator.RegisterTransient&lt;HomeView, HomeViewModel&gt;();
    ///     locator.RegisterSingleton&lt;MenuView, MenuViewModel&gt;();
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddFlowViews(this IServiceCollection services, Action<IViewLocator>? configure = null)
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
