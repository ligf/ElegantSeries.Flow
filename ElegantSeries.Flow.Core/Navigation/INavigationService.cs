using ElegantSeries.Flow.Core.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// Platform-agnostic navigation service that manages region-based navigation stacks,
/// KeepAlive caching, and ViewModel lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime model (v2.0):</b> every page gets its own <c>IServiceScope</c>. The ViewModel
/// is resolved from that scope, and leaving the page disposes the scope — releasing the
/// ViewModel and its whole Transient/Scoped dependency graph via the DI container.
/// </para>
/// <list type="table">
/// <listheader><term>Registration</term><description>Page lifetime</description></listheader>
/// <item><term>Transient</term><description>A new instance per page; disposed with the page scope.</description></item>
/// <item><term>Scoped</term><description>One instance per page; disposed with the page scope.</description></item>
/// <item><term>Singleton</term><description>Shared from the root container; a page scope never disposes it.</description></item>
/// </list>
/// <para>
/// A freshly resolved ViewModel <i>instance</i> cannot appear twice on navigation
/// stacks: navigating to a type whose instance is already the active page is a no-op
/// returning <see langword="true"/>, while navigating to a fresh instance that lives
/// elsewhere on a stack throws <see cref="InvalidOperationException"/> (typically a
/// Singleton registered ViewModel navigated to twice — navigate back to it instead).
/// Exception: re-navigating to a <i>cached</i> KeepAlive page reuses the cached instance
/// and may push it again (v1.x compatible); the shared page scope is disposed only
/// after its last stack/cache reference disappears.
/// </para>
/// <para>
/// Within one page scope the DI container stops disposing remaining services after the
/// first throwing disposable (platform behavior, sync and async alike). Across pages the
/// service still attempts every scope: a single failure is rethrown as-is, several are
/// wrapped in <see cref="AggregateException"/>.
/// </para>
/// </remarks>
public interface INavigationService
{
    // ────────────────────────────── Events ──────────────────────────────

    /// <summary>
    /// Raised when a region successfully navigates to a new active ViewModel.
    /// </summary>
    /// <remarks>Parameters: region name, activated ViewModel.</remarks>
    event Action<string, BaseViewModel>? RegionNavigated;

    /// <summary>
    /// Raised when the navigation service releases ownership of a page's ViewModel.
    /// </summary>
    /// <remarks>
    /// <para>Parameters: region name, released ViewModel.</para>
    /// <para>
    /// Ownership-released means the ViewModel was removed from navigation state and its
    /// page scope is being disposed. The event fires even if the scope disposal threw
    /// (the error is still reported to the caller); it does <i>not</i> mean every
    /// disposable inside the scope was released — the DI container stops a scope at the
    /// first throwing disposable. Singleton ViewModels are never disposed by a page
    /// scope and therefore never raise this event through page teardown.
    /// </para>
    /// </remarks>
    event Action<string, BaseViewModel>? ViewModelDisposed;

    /// <summary>
    /// Raised when a region's KeepAlive cache is cleared.
    /// </summary>
    /// <remarks>Parameter: region name.</remarks>
    event Action<string>? RegionCacheCleared;

    // ──────────────────────── Query Methods (Sync) ────────────────────────

    /// <summary>
    /// Determines whether back navigation is possible in the specified region.
    /// </summary>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <returns><see langword="true"/> if the region stack contains more than one page.</returns>
    bool CanGoBack(string regionName = "MainRegion");

    /// <summary>
    /// Gets the currently active ViewModel in the specified region.
    /// </summary>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <returns>The active ViewModel, or <see langword="null"/> if the region is empty.</returns>
    BaseViewModel? GetCurrentViewModel(string regionName = "MainRegion");

    /// <summary>
    /// Checks whether the active ViewModel in the specified region is of the given type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to check.</typeparam>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    bool IsActive<TViewModel>(string regionName = "MainRegion") where TViewModel : BaseViewModel;

    /// <summary>
    /// Gets the <see cref="NavigationMode"/> of the current page in the specified region.
    /// </summary>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <returns>The navigation mode, or <see langword="null"/> if the region is empty.</returns>
    NavigationMode? GetCurrentMode(string regionName = "MainRegion");

    // ─────────────────── Navigation Methods (Async) ───────────────────

    /// <summary>
    /// Navigates to the specified ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The target ViewModel type.</typeparam>
    /// <param name="regionName">The target region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="mode">The navigation mode. Defaults to <see cref="NavigationMode.New"/>.</param>
    /// <returns>
    /// <see langword="true"/> if navigation succeeded;
    /// <see langword="false"/> if cancelled by a guard.
    /// </returns>
    Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New)
        where TViewModel : BaseViewModel;

    /// <summary>
    /// Navigates to the specified ViewModel type with a strongly-typed parameter.
    /// </summary>
    /// <typeparam name="TViewModel">The target ViewModel type.</typeparam>
    /// <typeparam name="TParam">The parameter type.</typeparam>
    /// <param name="parameter">The navigation parameter.</param>
    /// <param name="regionName">The target region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="mode">The navigation mode. Defaults to <see cref="NavigationMode.New"/>.</param>
    /// <returns>
    /// <see langword="true"/> if navigation succeeded;
    /// <see langword="false"/> if cancelled by a guard.
    /// </returns>
    Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New)
        where TViewModel : BaseViewModel;

    /// <summary>
    /// Navigates back to the previous page in the specified region.
    /// </summary>
    /// <param name="regionName">The target region. Defaults to <c>"MainRegion"</c>.</param>
    /// <returns>
    /// <see langword="true"/> if back navigation succeeded;
    /// <see langword="false"/> if cancelled or the stack has one or fewer pages.
    /// </returns>
    Task<bool> GoBackAsync(string regionName = "MainRegion");

    // ──────────────── Cache Management (Sync then Async) ────────────────

    /// <summary>
    /// Synchronously clears the KeepAlive cache for the specified region.
    /// </summary>
    /// <param name="regionName">The navigation region to clear.</param>
    /// <remarks>
    /// <para>
    /// Page scopes still referenced by a navigation stack are deferred for disposal
    /// until their last reference is removed.
    /// </para>
    /// <para>
    /// The synchronous path disposes page scopes via <see cref="IDisposable.Dispose"/>.
    /// If a page scope contains a service that only implements
    /// <see cref="IAsyncDisposable"/>, the DI container throws
    /// <see cref="InvalidOperationException"/> directing the caller to the asynchronous
    /// APIs. Use <see cref="ClearCacheAsync"/> when pages need asynchronous cleanup.
    /// </para>
    /// </remarks>
    void ClearCache(string regionName);

    /// <summary>
    /// Synchronously clears the KeepAlive cache for all regions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Page scopes still referenced by navigation stacks are deferred for disposal
    /// until their last reference is removed.
    /// </para>
    /// <para>
    /// The synchronous path disposes page scopes via <see cref="IDisposable.Dispose"/>.
    /// If a page scope contains a service that only implements
    /// <see cref="IAsyncDisposable"/>, the DI container throws
    /// <see cref="InvalidOperationException"/> directing the caller to the asynchronous
    /// APIs. Use <see cref="ClearAllCacheAsync"/> when pages need asynchronous cleanup.
    /// </para>
    /// </remarks>
    void ClearAllCache();

    /// <summary>
    /// Asynchronously clears the KeepAlive cache for the specified region,
    /// preferring <see cref="IAsyncDisposable.DisposeAsync"/> when available.
    /// </summary>
    /// <param name="regionName">The navigation region to clear.</param>
    ValueTask ClearCacheAsync(string regionName);

    /// <summary>
    /// Asynchronously clears the KeepAlive cache for all regions,
    /// preferring <see cref="IAsyncDisposable.DisposeAsync"/> when available.
    /// </summary>
    ValueTask ClearAllCacheAsync();
}
