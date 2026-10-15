using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElegantSeries.Flow.Extensions;

/// <summary>
/// Extension methods for registering ElegantSeries.Flow navigation services.
/// </summary>
public static class FlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="INavigationService"/> with the specified lifetime.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">
    /// <see cref="ServiceLifetime.Singleton"/> (default): suitable for single-window
    /// applications or global navigation — one shared navigation stack.
    /// <see cref="ServiceLifetime.Scoped"/>: suitable for multi-window applications —
    /// each window scope gets its own <see cref="NavigationService"/> with isolated
    /// region stacks.
    /// <para>
    /// <b>Scoped contract (important):</b> Scoped mode isolates the <i>navigation stacks</i>
    /// per window scope, but page scopes are <i>not</i> nested inside the window scope —
    /// Microsoft.Extensions.DependencyInjection creates them as siblings under the root
    /// container (there is no public API to create a child scope from an existing scope).
    /// Consequences:
    /// <list type="bullet">
    /// <item>
    /// <description>Window-level <c>Scoped</c> services are <i>not</i> shared with pages.
    /// Use <c>Singleton</c> for state that pages must share with the window.</description>
    /// </item>
    /// <item>
    /// <description>Page ViewModels must <i>not</i> constructor-inject
    /// <see cref="INavigationService"/>: the injected instance would be a different scoped
    /// instance, not the one managing the page. Use the <c>Navigation</c> property
    /// (via <c>NavigationViewModelBase</c> or <see cref="INavigationViewModel"/>) instead —
    /// it is attached to the managing service on activation.</description>
    /// </item>
    /// </list>
    /// </para>
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lifetime"/> is <see cref="ServiceLifetime.Transient"/> or not a
    /// defined value. The navigation service is stateful (it owns the region stacks),
    /// so transient lifetime would hand each consumer an isolated, empty stack and
    /// silently break navigation.
    /// </exception>
    /// <remarks>
    /// <para><b>Registration conventions</b> (the page-level scope model disposes whatever
    /// the page scope created, so register with intent):</para>
    /// <list type="bullet">
    /// <item>
    /// <description>Page ViewModels: <c>Transient</c> or <c>Scoped</c> — a fresh instance
    /// per page, disposed with the page scope when the page is left.</description>
    /// </item>
    /// <item>
    /// <description>Application-level long-lived services: <c>Singleton</c> — owned by the
    /// root container and never disposed by a page scope.</description>
    /// </item>
    /// <item>
    /// <description>Page-private services: same scope as the page ViewModel
    /// (<c>Transient</c>/<c>Scoped</c>) so they are released together with the page.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Do not register an ordinary page as <c>Singleton</c> and expect it to be disposed
    /// when popped — it won't be: navigating to its type while its instance lives deeper
    /// on the region's stack pops back to the shared instance instead of pushing a duplicate.
    /// </para>
    /// <para>
    /// A <c>Singleton</c> ViewModel must not be the active page of two independent
    /// navigation services at once (e.g. two windows in <c>Scoped</c> mode): it has a
    /// single <c>Navigation</c> reference, so the second activation would hijack the
    /// first window's navigation routing. Prefer <c>Transient</c> (or window-scoped)
    /// for ViewModels with mutable page state.
    /// </para>
    /// <para>
    /// With <see cref="ServiceLifetime.Scoped"/>, the navigation service itself is scoped
    /// to the window scope (one stack per window), but page scopes are <i>not</i> nested
    /// inside it — see the Scoped contract in <paramref name="lifetime"/> above.
    /// </para>
    /// <para>
    /// Disposing the window scope disposes its navigation service. Page scopes are
    /// disposed by the navigation service when their pages leave. Never resolve a page
    /// ViewModel from the window scope directly and hand it to another window's
    /// navigation service — a page scope belongs to exactly one navigation service.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddElegantSeriesFlow(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        return AddElegantSeriesFlow(services, _ => { }, lifetime);
    }

    /// <summary>
    /// Registers the ElegantSeries.Flow navigation service with custom options.
    /// </summary>
    /// <remarks>
    /// The options are registered with <c>TryAddSingleton</c>: if
    /// <c>AddElegantSeriesFlow</c> is called multiple times, the first configuration
    /// wins and later ones are silently ignored. Configure once, typically at
    /// application startup.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="NavigationOptions"/>.</param>
    /// <param name="lifetime">Singleton (default, one service per app) or Scoped (one per window/scope).</param>
    public static IServiceCollection AddElegantSeriesFlow(
        this IServiceCollection services,
        Action<NavigationOptions> configure,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NavigationOptions();
        configure(options);
        services.TryAddSingleton(options);

        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.TryAddSingleton<INavigationService, NavigationService>();
                break;
            case ServiceLifetime.Scoped:
                services.TryAddScoped<INavigationService, NavigationService>();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(lifetime),
                    lifetime,
                    "INavigationService must be Singleton or Scoped. Transient would give each consumer an isolated, empty navigation stack.");
        }

        return services;
    }
}
