using System.Diagnostics.CodeAnalysis;
using static ElegantSeries.Flow.Navigation.NavigationDefaults;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Platform-agnostic navigation service that manages region-based navigation stacks,
/// KeepAlive caching, and ViewModel lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime model:</b> every page gets its own <c>IServiceScope</c>. The ViewModel
/// is resolved from that scope, and leaving the page disposes the scope — releasing the
/// ViewModel and its whole Transient/Scoped dependency graph via the DI container.
/// </para>
/// <list type="table">
/// <listheader><term>ViewModel lifetime</term><description>Page lifetime</description></listheader>
/// <item><term>Transient</term><description>A new instance per page; disposed with the page scope.</description></item>
/// <item><term>Scoped</term><description>One instance per page; disposed with the page scope.</description></item>
/// <item><term>Singleton</term><description>Shared from the root container; a page scope never disposes it.</description></item>
/// </list>
/// <para>
/// A ViewModel <i>instance</i> never appears twice on one region's stack.
/// Navigating with the default operation and retention (<see cref="NavigationOperation.Push"/>
/// + <see cref="NavigationRetention.Destroy"/>) while a page
/// of the same <i>type</i> is already the active page is a no-op returning
/// <see langword="true"/> (or a <i>refresh</i> when <c>refreshIfActive</c> is set —
/// the active instance's activation callbacks run again with the new parameter,
/// which is also persisted on the stack entry, without creating a new page
/// scope). An explicit operation or retention (<c>Replace</c>,
/// <c>Reset</c>, <c>KeepAlive</c>) takes precedence over this shortcut and
/// is always honored. Navigating to a type whose <i>instance</i> lives deeper
/// on the region's stack <i>pops back to it</i> — the pages above leave the
/// stack and the existing instance is re-activated with the new parameter.
/// This is how <c>Singleton</c> ViewModels and cached <c>KeepAlive</c> pages
/// behave (mainstream behavior, cf. Prism's <c>IsNavigationTarget</c>):
/// navigate away and the instance survives; navigate to its type and it is
/// reused instead of duplicated. The shared page scope is disposed only after
/// its last stack/cache reference disappears.
/// </para>
/// <para>
/// Navigating to an instance that lives on a <i>different</i> region's stack throws
/// <see cref="InvalidOperationException"/>: a ViewModel has a single
/// <see cref="INavigationViewModel.Navigation"/> reference and cannot be active in
/// two regions at once.
/// </para>
/// <para>
/// <b>Cancellation:</b> <c>NavigateToAsync</c> / <c>GoBackAsync</c> accept a
/// <see cref="CancellationToken"/> that is honored while the request is still
/// pending: cancellation withdraws the request — it never executes — and the
/// awaiting caller throws <see cref="OperationCanceledException"/>. Cancellation
/// during the leave guard still counts as pending (the guard is raced against the
/// token). Once the departing page's <c>OnNavigatedFrom</c> has started, the
/// departure is no longer interrupted: the callback runs to completion. A
/// cancellation that arrives afterwards is still honored where the executing
/// step checks the token (while the target is being resolved and just before
/// the commit); withdrawing there re-activates the departing page — its
/// <c>OnNavigatedTo</c> runs again — instead of completing the departure.
/// (Those checkpoints exist on the <c>NavigateToAsync</c> hop only;
/// <c>GoBackAsync</c> has none — see its parameter documentation.)
/// A target page is constructed only when its request is executed, so a withdrawn
/// request never leaves a page scope behind.
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
/// <para>
/// <b>Region names:</b> every <c>regionName</c> parameter on this interface is
/// normalized the same way — surrounding whitespace is trimmed, and names are
/// compared ordinally (case-sensitive), as befits programmatic identifiers.
/// <c>" MainRegion "</c> and <c>"MainRegion"</c> address the same region;
/// <c>"mainregion"</c> is a different one. Null, empty, and whitespace-only
/// names are rejected with <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// This interface does not extend <see cref="IDisposable"/> or
/// <see cref="IAsyncDisposable"/>: the service is typically a singleton owned by the
/// DI container, which disposes it. To dispose explicitly (e.g. in tests), cast to
/// the concrete <c>NavigationService</c> or to <see cref="IAsyncDisposable"/>.
/// </para>
/// </remarks>
public interface INavigationService
{
    // ────────────────────────────── Events ──────────────────────────────

    /// <summary>
    /// Raised when a region's active ViewModel comes to rest as the region's
    /// current page.
    /// </summary>
    /// <remarks>
    /// <para>Parameters: region name, activated ViewModel.</para>
    /// <para>
    /// The event is raised when the page's activation callbacks have completed and
    /// no further navigation request is pending for the region. A page that is
    /// passed through by a redirect chain — its activation immediately produces
    /// another request — does <i>not</i> raise this event: hosts therefore never
    /// build views for intermediate pages of a chain.
    /// </para>
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
    event Action<string, INavigationViewModel>? ViewModelReleased;

    /// <summary>
    /// Raised when a region's KeepAlive cache is cleared and at least one cached
    /// entry was actually removed. Clearing a cache that holds no entries for the
    /// region does not raise this event. When the service itself is disposed,
    /// the event is also raised for every region that still held stack, cache
    /// or pending entries at shutdown.
    /// </summary>
    /// <remarks>Parameter: region name.</remarks>
    event Action<string>? RegionCacheCleared;

    /// <summary>
    /// Raised when a navigation event subscriber (<see cref="RegionNavigated"/>,
    /// <see cref="ViewModelReleased"/>, <see cref="RegionCacheCleared"/>) throws,
    /// and for navigation failures that have no awaiting caller to throw to:
    /// a leave guard that faults after its caller has withdrawn, a disposal-gate
    /// timeout, or a callback chain stopped by
    /// <see cref="NavigationOptions.MaxNavigationChainLength"/>. The exception
    /// is swallowed to protect navigation state; subscribe to this event for
    /// diagnostics.
    /// </summary>
    /// <remarks>Parameter: the reported exception.</remarks>
    event Action<Exception>? SubscriberError;

    /// <summary>
    /// Subscribes to <see cref="RegionNavigated"/>. Dispose the returned object
    /// to unsubscribe — safer than manual <c>-=</c> for hosts with complex lifetimes.
    /// </summary>
    IDisposable SubscribeRegionNavigated(Action<string, INavigationViewModel> handler);

    /// <summary>
    /// Subscribes to <see cref="ViewModelReleased"/>. Dispose the returned object
    /// to unsubscribe.
    /// </summary>
    IDisposable SubscribeViewModelReleased(Action<string, INavigationViewModel> handler);

    /// <summary>
    /// Subscribes to <see cref="RegionCacheCleared"/>. Dispose the returned object
    /// to unsubscribe.
    /// </summary>
    IDisposable SubscribeRegionCacheCleared(Action<string> handler);

    /// <summary>
    /// Subscribes to <see cref="SubscriberError"/>. Dispose the returned object
    /// to unsubscribe.
    /// </summary>
    IDisposable SubscribeSubscriberError(Action<Exception> handler);

    // ──────────────────────── Query Methods (Sync) ────────────────────────

    /// <summary>
    /// Determines whether back navigation is possible in the specified region.
    /// </summary>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <returns><see langword="true"/> if the region stack contains more than one page.</returns>
    bool CanGoBack(string regionName = DefaultRegionName);

    /// <summary>
    /// Gets the currently active ViewModel in the specified region.
    /// </summary>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <returns>The active ViewModel, or <see langword="null"/> if the region is empty.</returns>
    INavigationViewModel? GetCurrentViewModel(string regionName = DefaultRegionName);

    /// <summary>
    /// Checks whether the active ViewModel in the specified region is of the given type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to check.</typeparam>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    bool IsActive<TViewModel>(string regionName = DefaultRegionName) where TViewModel : INavigationViewModel;

    /// <summary>
    /// Gets the <see cref="NavigationRetention"/> the current page in the specified
    /// region was recorded with.
    /// </summary>
    /// <param name="regionName">The navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <returns>The current page's retention, or <see langword="null"/> if the region is empty.</returns>
    NavigationRetention? GetCurrentRetention(string regionName = DefaultRegionName);

    // ─────────────────── Navigation Methods (Async) ───────────────────

    /// <summary>
    /// Navigates to the specified ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The target ViewModel type.</typeparam>
    /// <param name="regionName">The target region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="operation">The stack operation. Defaults to <see cref="NavigationOperation.Push"/>.</param>
    /// <param name="retention">What happens to the page when it later leaves. Defaults to <see cref="NavigationRetention.Destroy"/>.</param>
    /// <param name="refreshIfActive">
    /// When <see langword="true"/> and a page of type <typeparamref name="TViewModel"/> is
    /// already the active page, the page is <i>refreshed</i> instead of being a silent
    /// no-op: nothing is pushed and no new page scope is created — the active instance's
    /// activation callbacks run again with the new parameter and
    /// <see cref="RegionNavigated"/> is raised. When the target type is not active this
    /// flag has no effect. Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cooperative cancellation: while this request is still pending, cancellation
    /// withdraws it and throws <see cref="OperationCanceledException"/>. Once the
    /// departing page's <c>OnNavigatedFrom</c> has started, the callback runs to
    /// completion and the departure is no longer interrupted; a cancellation that
    /// arrives afterwards is honored only where the executing step still checks
    /// the token (target resolution, just before the commit), and withdrawing
    /// there re-activates the departing page instead of completing the departure.
    /// </param>
    /// <returns>
    /// The fate of this request: <see langword="true"/> if it was executed;
    /// <see langword="false"/> if it was superseded by a later request in the same
    /// page window, vetoed by the current page's leave guard, or discarded because
    /// the service was disposed.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before this
    /// call completes; cancellation while the request is still pending also
    /// withdraws the request.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resolved ViewModel instance already lives on another
    /// region's stack.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the service has been disposed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Window model.</b> A navigation call registers a <i>request</i>; it does not
    /// execute immediately. A page's lifecycle window runs from its activation until
    /// its <c>OnNavigatedFrom</c> callback completes. Requests registered while a
    /// window is open — from the page's own <c>OnNavigatedTo</c>/<c>OnNavigatedFrom</c>
    /// callbacks, from page code such as button handlers, or from anywhere else —
    /// supersede one another: only the last request registered before the window
    /// closes is executed, and superseded requests never construct a ViewModel.
    /// The executed request's target page is always constructed and runs its full
    /// lifecycle, so callback redirect chains advance one constructed page at a
    /// time. A request registered while the region rests on a page starts that
    /// page's departure immediately.
    /// </para>
    /// <para>
    /// Calls made from within a lifecycle callback return immediately: with
    /// <see langword="true"/> when the request was registered, or with
    /// <see langword="false"/> when the service has already been disposed and
    /// the request was discarded. Their outcome is observable through
    /// <see cref="RegionNavigated"/>. Calls made from anywhere else complete
    /// with the request's own fate as described above.
    /// </para>
    /// <para>
    /// Keep ViewModel constructors lightweight: a page is constructed only when its
    /// request is actually executed. Put initialization work in
    /// <c>OnNavigatedToAsync</c> instead, where the page is guaranteed to be active.
    /// </para>
    /// <para>
    /// Consecutive hops driven by callback-registered requests are bounded by
    /// <see cref="NavigationOptions.MaxNavigationChainLength"/>: a chain that
    /// exceeds it is stopped (the current page stays active) and the stop is
    /// reported through <see cref="SubscriberError"/>, since the requests that
    /// drive such a chain have no awaiting caller to throw to.
    /// </para>
    /// </remarks>
    Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,

        NavigationRetention retention = NavigationRetention.Destroy,
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
    /// <param name="operation">The stack operation. Defaults to <see cref="NavigationOperation.Push"/>.</param>
    /// <param name="retention">What happens to the page when it later leaves. Defaults to <see cref="NavigationRetention.Destroy"/>.</param>
    /// <param name="refreshIfActive">
    /// When <see langword="true"/> and a page of type <typeparamref name="TViewModel"/> is
    /// already the active page, the page is <i>refreshed</i> with
    /// <paramref name="parameter"/> instead of being a silent no-op: nothing is pushed and
    /// no new page scope is created — the active instance's activation callbacks run again
    /// and <see cref="RegionNavigated"/> is raised. When the target type is not active this
    /// flag has no effect. Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cooperative cancellation: while this request is still pending, cancellation
    /// withdraws it and throws <see cref="OperationCanceledException"/>. Once the
    /// departing page's <c>OnNavigatedFrom</c> has started, the callback runs to
    /// completion and the departure is no longer interrupted; a cancellation that
    /// arrives afterwards is honored only where the executing step still checks
    /// the token (target resolution, just before the commit), and withdrawing
    /// there re-activates the departing page instead of completing the departure.
    /// </param>
    /// <returns>
    /// The fate of this request: <see langword="true"/> if it was executed;
    /// <see langword="false"/> if it was superseded by a later request in the same
    /// page window, vetoed by the current page's leave guard, or discarded because
    /// the service was disposed.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before this
    /// call completes; cancellation while the request is still pending also
    /// withdraws the request.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resolved ViewModel instance already lives on another
    /// region's stack.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the service has been disposed.
    /// </exception>
    /// <remarks>
    /// See the window-model remarks on
    /// <see cref="NavigateToAsync{TViewModel}(string, NavigationOperation, NavigationRetention, bool, CancellationToken)"/>.
    /// </remarks>
    Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,

        NavigationRetention retention = NavigationRetention.Destroy,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Navigates back to the previous page in the specified region.
    /// </summary>
    /// <param name="regionName">The target region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="cancellationToken">
    /// Cooperative cancellation: while this request is still pending, cancellation
    /// withdraws it and throws <see cref="OperationCanceledException"/>. Once the
    /// departing page's <c>OnNavigatedFrom</c> has started, the token no longer
    /// withdraws the request: a back navigation has no resolution or pre-commit
    /// checkpoint, so the callback runs to completion and the back navigation is
    /// carried out (the awaiting caller still observes
    /// <see cref="OperationCanceledException"/> from its own token). This is
    /// narrower than <c>NavigateToAsync</c>, whose executing hop keeps honoring
    /// the token until it commits.
    /// </param>
    /// <returns>
    /// The fate of this request: <see langword="true"/> if it was executed;
    /// <see langword="false"/> if it was superseded by a later request in the same
    /// page window, vetoed by the current page's leave guard, the stack has one or
    /// fewer pages, or the service was disposed.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before this
    /// call completes; cancellation while the request is still pending also
    /// withdraws the request.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the service has been disposed.
    /// </exception>
    /// <remarks>
    /// A back request participates in the same window model as
    /// <see cref="NavigateToAsync{TViewModel}(string, NavigationOperation, NavigationRetention, bool, CancellationToken)"/>:
    /// a <c>NavigateToAsync</c> request registered later in the same page window
    /// supersedes it, and a back request registered during the current page's
    /// <c>OnNavigatedFrom</c> supersedes the request that started the departure.
    /// </remarks>
    Task<bool> GoBackAsync(string regionName = DefaultRegionName, CancellationToken cancellationToken = default);

    // ──────────────── Cache Management (Sync then Async) ────────────────

    /// <summary>
    /// Synchronously clears the KeepAlive cache for the specified region.
    /// </summary>
    /// <param name="regionName">The navigation region to clear.</param>
    /// <exception cref="TimeoutException">
    /// Thrown when the navigation lock cannot be acquired within
    /// <see cref="NavigationOptions.SyncLockTimeout"/> (default 2 seconds).
    /// </exception>
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
    /// <para>
    /// Lifecycle contract: only cached pages no longer referenced by the navigation
    /// stack have their lifecycle tokens cancelled; the token of a still-active page
    /// is left untouched. This method does NOT wait for in-flight async lifecycle
    /// callbacks (e.g. a running <c>OnNavigatedFromAsync</c>) to complete. Callers
    /// must ensure their callbacks observe cancellation promptly. Use
    /// <see cref="ClearCacheAsync"/> to wait for in-flight callbacks with a timeout.
    /// </para>
    /// <para>
    /// This method blocks until any in-flight navigation transition releases the
    /// navigation lock, for at most <see cref="NavigationOptions.SyncLockTimeout"/>.
    /// Prefer <see cref="ClearCacheAsync"/> on UI threads to avoid
    /// stalling the interface.
    /// </para>
    /// <para>
    /// Reentrancy: throws <see cref="InvalidOperationException"/> if called from
    /// within a page lifecycle callback (e.g. <c>OnNavigatedToAsync</c> or
    /// <c>OnNavigatedFromAsync</c>). Call it outside the callback instead.
    /// </para>
    /// </remarks>
    void ClearCache(string regionName);

    /// <summary>
    /// Synchronously clears the KeepAlive cache for all regions.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// Thrown when the navigation lock cannot be acquired within
    /// <see cref="NavigationOptions.SyncLockTimeout"/> (default 2 seconds).
    /// </exception>
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
    /// <para>
    /// This method blocks until any in-flight navigation transition releases the
    /// navigation lock, for at most <see cref="NavigationOptions.SyncLockTimeout"/>.
    /// Prefer <see cref="ClearAllCacheAsync"/> on UI threads to avoid
    /// stalling the interface.
    /// </para>
    /// <para>
    /// Lifecycle contract: only cached pages no longer referenced by the navigation
    /// stack have their lifecycle tokens cancelled; the token of a still-active page
    /// is left untouched. This method does NOT wait for in-flight async lifecycle
    /// callbacks to complete. Use <see cref="ClearAllCacheAsync"/> to wait for
    /// in-flight callbacks with a timeout.
    /// </para>
    /// <para>
    /// Reentrancy: throws <see cref="InvalidOperationException"/> if called from
    /// within a page lifecycle callback. Call it outside the callback instead.
    /// </para>
    /// </remarks>
    void ClearAllCache();

    /// <summary>
    /// Asynchronously clears the KeepAlive cache for the specified region,
    /// preferring <see cref="IAsyncDisposable.DisposeAsync"/> when available.
    /// </summary>
    /// <param name="regionName">The navigation region to clear.</param>
    /// <remarks>
    /// <para>
    /// Reentrancy: throws <see cref="InvalidOperationException"/> if called from
    /// within a page lifecycle callback (e.g. <c>OnNavigatedFromAsync</c>).
    /// Call it outside the callback instead.
    /// </para>
    /// </remarks>
    ValueTask ClearCacheAsync(string regionName);

    /// <summary>
    /// Asynchronously clears the KeepAlive cache for all regions,
    /// preferring <see cref="IAsyncDisposable.DisposeAsync"/> when available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reentrancy: throws <see cref="InvalidOperationException"/> if called from
    /// within a page lifecycle callback. Call it outside the callback instead.
    /// </para>
    /// </remarks>
    ValueTask ClearAllCacheAsync();
}
