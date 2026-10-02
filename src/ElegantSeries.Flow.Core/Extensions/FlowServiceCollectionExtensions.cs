using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElegantSeries.Flow.Core.Extensions;

/// <summary>
/// Extension methods for registering ElegantSeries.Flow navigation services.
/// </summary>
public static class FlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="INavigationService"/> as a singleton.
    /// Suitable for single-window applications or global navigation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
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
    /// </remarks>
    public static IServiceCollection AddFlowNavigation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<INavigationService, NavigationService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="INavigationService"/> as a scoped service.
    /// Suitable for multi-window applications where each window has its own scope
    /// with an isolated navigation stack.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Each window scope gets its own <see cref="NavigationService"/> with isolated
    /// region stacks. Page scopes nest inside the window scope:
    /// </para>
    /// <code>
    /// root container
    ///  └─ window scope (AddScopedFlowNavigation → INavigationService per window)
    ///       ├─ page scope (page 1: ViewModel + its Transient/Scoped graph)
    ///       └─ page scope (page 2: ViewModel + its Transient/Scoped graph)
    /// </code>
    /// <para>
    /// Disposing the window scope disposes its navigation service and, through it, every
    /// page scope below. Never resolve a page ViewModel from the window scope directly and
    /// hand it to another window's navigation service — a page scope belongs to exactly
    /// one navigation service.
    /// </para>
    /// <para>See <see cref="AddFlowNavigation"/> for the registration conventions.</para>
    /// </remarks>
    public static IServiceCollection AddScopedFlowNavigation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<INavigationService, NavigationService>();
        return services;
    }
}
