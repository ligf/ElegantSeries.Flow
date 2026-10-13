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
    /// With <see cref="ServiceLifetime.Scoped"/>, page scopes nest inside the window scope:
    /// </para>
    /// <code>
    /// root container
    ///  └─ window scope (AddElegantSeriesFlow(ServiceLifetime.Scoped) → INavigationService per window)
    ///       ├─ page scope (page 1: ViewModel + its Transient/Scoped graph)
    ///       └─ page scope (page 2: ViewModel + its Transient/Scoped graph)
    /// </code>
    /// <para>
    /// Disposing the window scope disposes its navigation service and, through it, every
    /// page scope below. Never resolve a page ViewModel from the window scope directly and
    /// hand it to another window's navigation service — a page scope belongs to exactly
    /// one navigation service.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddElegantSeriesFlow(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);

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
