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
    /// locator. Runs once, when the singleton is first resolved.
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
    /// The navigation service itself (<c>ElegantSeries.Flow.Core</c>) is registered
    /// separately; this method only covers the Avalonia view layer.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddFlowViews(locator =>
    /// {
    ///     locator.Register&lt;HomeView, HomeViewModel&gt;();
    ///     locator.Register&lt;SettingsView, SettingsViewModel&gt;();
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
