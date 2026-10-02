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
/// returning <see langword="true"/> (or a <i>refresh</i> when <c>refreshIfActive</c> is
/// set — the active instance's activation callbacks run again with the new parameter,
/// without creating a new page scope), while navigating to a fresh instance that lives
/// elsewhere on a stack throws <see cref="InvalidOperationException"/> (typically a
/// Singleton registered ViewModel navigated to twice — navigate back to it instead).
/// Exception: re-navigating to a <i>cached</i> KeepAlive page reuses the cached instance
/// and may push it again (v1.x compatible), so the same ViewModel can appear several
/// times on one stack and back navigation passes through it repeatedly; the shared page
/// scope is disposed only after its last stack/cache reference disappears.
/// </para>
/// <para>
/// <b>Cancellation:</b> <c>NavigateToAsync</c> / <c>GoBackAsync</c> accept a
/// <see cref="CancellationToken"/> that is honored until the region stack is updated.
/// Cancellation before that point releases the created page scope (if any) and throws
/// <see cref="OperationCanceledException"/>. The from-guard runs before the page scope
/// is created, so cancellation during the guard never leaves a scope behind; every
/// cancellation point after scope creation releases it. Once the stack has been updated
/// the transition runs to completion and the token is ignored.
/// </para>
/// <para>
/// <b>Transition order</b> (fixed): <c>OnNavigatedFrom</c> →
/// page-scope disposal → <c>OnNavigatedTo</c> → events. UI hosts should swap views on
/// <see cref="RegionNavigated"/>; the view is not guaranteed to be attached yet when
/// <c>OnNavigatedTo</c> runs. ViewModels that need asynchronous work during the
/// transition should implement <see cref="INavigationAwareAsync"/> instead of
/// <see cref="INavigationAware"/>.
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
    /// <remarks>
    /// <para>Parameters: region name, activated ViewModel.</para>
    /// <para>
    /// This is also raised for a <i>refresh</i> navigation (same type already active with
    /// <c>refreshIfActive</c>): the page is not pushed again, but subscribers are notified
    /// that its activation callbacks ran again with a new parameter.
    /// </para>
    /// <para>
    /// The navigation service is typically a long-lived singleton: views/hosts that
    /// subscribe must unsubscribe when detached (or dispose their subscription), otherwise
    /// the service keeps the subscriber alive.
    /// </para>
    /// </remarks>
    event Action<string, INavigationViewModel>? RegionNavigated;

    /// <summary>
    /// Raised when the navigation service releases ownership of a page's ViewModel
    /// (end of page ownership — not necessarily "the instance was disposed"; see remarks).
    /// </summary>
    /// <remarks>
    /// <para>Parameters: region name, released ViewModel.</para>
    /// <para>
    /// Ownership-released means the ViewModel was removed from navigation state and its
    /// page scope is being disposed. The event fires even if the scope disposal threw
    /// (the error is still reported to the caller); it does <i>not</i> mean every
    /// disposable inside the scope was released — the DI container stops a scope at the
    /// first throwing disposable. The event also fires for Singleton ViewModels when
    /// their page is torn down: the page scope is disposed, but the shared instance
    /// itself survives because it is owned by the root container.
    /// </para>
    /// <para>
    /// The navigation service is typically a long-lived singleton: views/hosts that
    /// subscribe must unsubscribe when detached (or dispose their subscription), otherwise
    /// the service keeps the subscriber alive.
    /// </para>
    /// </remarks>
    event Action<string, INavigationViewModel>? ViewModelDisposed;

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
    INavigationViewModel? GetCurrentViewModel(string regionName = "MainRegion");

    /// <summary>
    /// Checks whether the active ViewModel in the specified region is of the given type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to check.</typeparam>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    bool IsActive<TViewModel>(string regionName = "MainRegion") where TViewModel : INavigationViewModel;

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
    /// <param name="refreshIfActive">
    /// When <see langword="true"/> and a page of type <typeparamref name="TViewModel"/> is
    /// already the active page, the page is <i>refreshed</i> instead of being a silent
    /// no-op: nothing is pushed and no new page scope is created — the active instance's
    /// activation callbacks run again with the new parameter and
    /// <see cref="RegionNavigated"/> is raised. When the target type is not active this
    /// flag has no effect. Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cooperative cancellation, honored until the region stack is updated: cancellation
    /// before that point releases the created page scope (if any) and throws
    /// <see cref="OperationCanceledException"/>. Once the stack has been updated the
    /// transition runs to completion and the token is ignored.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if navigation succeeded;
    /// <see langword="false"/> if cancelled by a guard.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the region
    /// stack is updated.
    /// </exception>
    Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Navigates to the specified ViewModel type with a strongly-typed parameter.
    /// </summary>
    /// <typeparam name="TViewModel">The target ViewModel type.</typeparam>
    /// <typeparam name="TParam">The parameter type.</typeparam>
    /// <param name="parameter">The navigation parameter.</param>
    /// <param name="regionName">The target region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="mode">The navigation mode. Defaults to <see cref="NavigationMode.New"/>.</param>
    /// <param name="refreshIfActive">
    /// When <see langword="true"/> and a page of type <typeparamref name="TViewModel"/> is
    /// already the active page, the page is <i>refreshed</i> with
    /// <paramref name="parameter"/> instead of being a silent no-op: nothing is pushed and
    /// no new page scope is created — the active instance's activation callbacks run again
    /// and <see cref="RegionNavigated"/> is raised. When the target type is not active this
    /// flag has no effect. Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cooperative cancellation, honored until the region stack is updated: cancellation
    /// before that point releases the created page scope (if any) and throws
    /// <see cref="OperationCanceledException"/>. Once the stack has been updated the
    /// transition runs to completion and the token is ignored.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if navigation succeeded;
    /// <see langword="false"/> if cancelled by a guard.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the region
    /// stack is updated.
    /// </exception>
    Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Navigates back to the previous page in the specified region.
    /// </summary>
    /// <param name="regionName">The target region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="cancellationToken">
    /// Cooperative cancellation, honored until the region stack is updated: cancellation
    /// before that point throws <see cref="OperationCanceledException"/>. Once the stack
    /// has been updated the transition runs to completion and the token is ignored.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if back navigation succeeded;
    /// <see langword="false"/> if cancelled or the stack has one or fewer pages.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the region
    /// stack is updated.
    /// </exception>
    Task<bool> GoBackAsync(string regionName = "MainRegion", CancellationToken cancellationToken = default);

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
