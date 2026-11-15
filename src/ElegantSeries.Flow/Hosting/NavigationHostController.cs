using System.Runtime.CompilerServices;
using ElegantSeries.Flow.Locating;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Threading;

namespace ElegantSeries.Flow.Hosting;

/// <summary>
/// UI-framework-agnostic navigation-host logic used by the platform
/// <c>NavigationHost</c> controls (WPF, Avalonia).
/// </summary>
/// <remarks>
/// <para>
/// Generic over the view type so the region filtering, UI-thread marshaling,
/// view caching, and failure handling are unit-testable without a UI runtime
/// (tests use <c>object</c> as the view type).
/// </para>
/// <para>
/// View lifetime is bound to ViewModel lifetime via a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> keyed by ViewModel (values are
/// boxed because the table demands a parameterless constructor on its value type):
/// a cached view is reused while its ViewModel is alive (KeepAlive navigation
/// reuses views for free), and becomes collectible together with a non-KeepAlive
/// ViewModel. The host never owns views and never needs explicit cache cleanup.
/// </para>
/// <para>
/// Failure handling: the view factory may throw (unregistered ViewModel type,
/// misconfigured host, or a throwing factory), and the show and clear
/// callbacks run view code too — assigning a view's DataContext triggers the
/// view's own initialization, and clearing detaches the outgoing view.
/// Failures at any of these steps never corrupt the currently shown content;
/// they are reported through the <c>onViewCreationFailed</c> hook — with one
/// exception: a clear failure when no page has been shown has no page to
/// attribute it to and is swallowed unreported. They are
/// deliberately not rethrown: the navigation service isolates event subscribers and swallows their exceptions
/// by design, so throwing could never fail fast — the hook is the observable channel.
/// The hook is also the terminal channel: an exception thrown by the hook
/// itself is swallowed and never propagates to the event raiser, a property
/// setter, or the dispatcher.
/// </para>
/// <para>
/// Threading: <see cref="Attach(INavigationService)"/>/<see cref="Detach"/>/
/// <see cref="Close"/> are serialized under one lock; the navigation callback
/// is filtered and then marshaled to the UI thread through
/// <see cref="IDispatcher"/>. No lock is held while user code (the view
/// factory or the show callback) runs.
/// </para>
/// </remarks>
/// <typeparam name="TView">The view type.</typeparam>
internal sealed class NavigationHostController<TView> where TView : class
{
    private readonly IDispatcher _dispatcher;
    private readonly Func<string> _regionNameProvider;
    private readonly Func<ViewCreationContext, TView> _viewFactory;
    private readonly Action<TView, INavigationViewModel> _showView;
    private readonly Action _clearView;
    private readonly Action<INavigationViewModel, Exception>? _onViewCreationFailed;
    // StrongBox works around a trim-analysis papercut: ConditionalWeakTable<TKey, TValue>
    // demands a public parameterless constructor on TValue (for GetOrCreateValue),
    // which we never call. Boxing the view keeps TView free of constructor constraints.
    private readonly ConditionalWeakTable<INavigationViewModel, StrongBox<TView>> _viewCache = new();
    private readonly Lock _eventLock = new();
    private INavigationService? _navigationService;
    private IDisposable? _navigatedSubscription;
    private bool _closed;
    // The page the host most recently showed, used to attribute a
    // clear failure (a clear has no incoming page of its own). Written
    // after a successful show and cleared after a successful clear;
    // show and clear only ever run on the UI thread, so no lock is
    // needed for it.
    private INavigationViewModel? _lastShownViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationHostController{TView}"/> class.
    /// </summary>
    /// <param name="dispatcher">Marshals view updates to the UI thread.</param>
    /// <param name="regionNameProvider">
    /// Returns the region this host displays. Evaluated per event so renames take
    /// effect immediately. It is evaluated on the thread raising the navigation
    /// event (the receipt-time filter, before any marshaling) as well as on the
    /// UI thread (the queued path's re-check and <see cref="Refresh"/>), so it
    /// must be thread-safe, cheap, and must not touch thread-affine UI state —
    /// mirror the name into a plain (volatile) field, as the platform hosts do.
    /// The returned name is compared ordinally against the service's normalized
    /// event names, so it must be returned in the service's normalized form
    /// (trimmed).
    /// </param>
    /// <param name="viewFactory">
    /// Creates the view for a page, given its <see cref="ViewCreationContext"/>.
    /// May throw (for example
    /// <see cref="InvalidOperationException"/> for an unregistered ViewModel type);
    /// failures are reported via <paramref name="onViewCreationFailed"/> and the
    /// previously shown content is kept.
    /// </param>
    /// <param name="showView">Displays a view for a ViewModel. Always runs on the UI thread. May throw (it runs view code); failures are reported via <paramref name="onViewCreationFailed"/> and the previously shown content is kept.</param>
    /// <param name="clearView">Clears the displayed content. Always runs on the UI thread. May throw (it runs the outgoing view's code); failures are reported via <paramref name="onViewCreationFailed"/> against the most recently shown page, or swallowed unreported when no page has been shown.</param>
    /// <param name="onViewCreationFailed">Optional hook invoked when creating or displaying a view fails — <paramref name="viewFactory"/>, <paramref name="showView"/>, or <paramref name="clearView"/> throws. Exceptions thrown by the hook are swallowed: the hook is the terminal failure channel.</param>
    public NavigationHostController(
        IDispatcher dispatcher,
        Func<string> regionNameProvider,
        Func<ViewCreationContext, TView> viewFactory,
        Action<TView, INavigationViewModel> showView,
        Action clearView,
        Action<INavigationViewModel, Exception>? onViewCreationFailed = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _regionNameProvider = regionNameProvider ?? throw new ArgumentNullException(nameof(regionNameProvider));
        _viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        _showView = showView ?? throw new ArgumentNullException(nameof(showView));
        _clearView = clearView ?? throw new ArgumentNullException(nameof(clearView));
        _onViewCreationFailed = onViewCreationFailed;
    }

    /// <summary>
    /// Subscribes to the navigation service's <see cref="INavigationService.RegionNavigated"/>
    /// event, replacing any previous subscription. After <see cref="Close"/> has
    /// been called, <see cref="Attach"/> is a no-op: a closed controller can
    /// never be subscribed again.
    /// </summary>
    /// <param name="navigationService">The navigation service to observe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="navigationService"/> is <see langword="null"/>.</exception>
    public void Attach(INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(navigationService);

        lock (_eventLock)
        {
            if (_closed)
            {
                return;
            }

            DetachLocked();
            _navigationService = navigationService;
            _navigatedSubscription = navigationService.SubscribeRegionNavigated(OnRegionNavigated);
        }
    }

    /// <summary>
    /// Unsubscribes from the navigation service. Safe to call multiple times.
    /// The controller can be attached again afterwards; for a permanent,
    /// irreversible shutdown use <see cref="Close"/>.
    /// </summary>
    public void Detach()
    {
        lock (_eventLock)
        {
            DetachLocked();
        }
    }

    /// <summary>
    /// Unsubscribes from the navigation service and permanently closes the
    /// controller: later <see cref="Attach"/> calls are ignored. Safe to call
    /// multiple times, from any thread. The platform hosts call this from
    /// their <c>Dispose</c>: because the closed check and the subscription
    /// change run under the same lock, a property change that passed the
    /// host's own disposed check just before disposal cannot re-subscribe
    /// the controller after disposal completes.
    /// </summary>
    /// <remarks>
    /// The guarantee boundary: closure stops every application of a
    /// navigation that has not already passed its final closed check —
    /// applications starting after closure, and dispatcher callbacks
    /// queued before it, are dropped at those checks and run no user code.
    /// An application that passed its final check on another thread just
    /// before <see cref="Close"/> runs may still deliver its one show,
    /// clear, or failure callback: the checks and the UI callbacks are
    /// deliberately not serialized under a single lock (no lock is held
    /// while user code runs), so closure is not a barrier over operations
    /// already in flight. The platform hosts tolerate that single late
    /// callback: it only sets or clears the content of a host that is
    /// already being discarded, and no subscription survives it.
    /// </remarks>
    public void Close()
    {
        lock (_eventLock)
        {
            _closed = true;
            DetachLocked();
        }
    }

    /// <summary>
    /// Re-displays the current page of the region returned by the region-name
    /// provider. Call when the region name changes: the host immediately shows
    /// the new region's current content instead of keeping the previous region's
    /// stale view; a region with no pages clears the host. Must be called on the
    /// UI thread. Does nothing when detached.
    /// </summary>
    public void Refresh()
    {
        INavigationService? service;
        lock (_eventLock)
        {
            service = _navigationService;
        }

        if (service is null)
        {
            return;
        }

        var regionName = _regionNameProvider();
        if (string.IsNullOrWhiteSpace(regionName))
        {
            // A transient empty binding value clears the host instead of
            // throwing from GetCurrentViewModel's argument validation.
            ClearView();
            return;
        }

        var viewModel = service.GetCurrentViewModel(regionName);
        if (viewModel is null)
        {
            ClearView();
            return;
        }

        ApplyNavigation(viewModel, service, regionName);
    }

    private void ClearView()
    {
        if (IsClosed())
        {
            return;
        }

        try
        {
            _clearView();
            _lastShownViewModel = null;
        }
        catch (Exception ex)
        {
            // Clearing runs the outgoing view's code (detaching it can
            // throw): the failure is reported through the hook against
            // the page the host most recently showed — the page still
            // on screen — and never escapes Refresh (a property setter
            // on the platform hosts). When nothing has been shown there
            // is no page to attribute the failure to; it is swallowed,
            // like a hook failure, rather than rethrown.
            if (_lastShownViewModel is not null)
            {
                ReportViewFailure(_lastShownViewModel, ex);
            }
        }
    }

    private bool IsClosed()
    {
        lock (_eventLock)
        {
            return _closed;
        }
    }

    private void DetachLocked()
    {
        if (_navigationService is not null)
        {
            _navigatedSubscription?.Dispose();
            _navigatedSubscription = null;
            _navigationService = null;
        }
    }

    private void OnRegionNavigated(string regionName, INavigationViewModel viewModel)
    {
        if (!string.Equals(regionName, _regionNameProvider(), StringComparison.Ordinal))
        {
            return;
        }

        // Defense against stale events (e.g. reentrant navigation): skip the
        // event when it no longer names the region's current page, judged by
        // the same rule the queued path re-applies at execution time (see
        // IsSuperseded). The service itself already suppresses stale events
        // at the source; this is defense in depth for third-party
        // implementations and for state changed by an earlier subscriber of
        // the same event — one that disposes the service mid-dispatch, for
        // example, emptying the region before this handler runs.
        INavigationService? service;
        lock (_eventLock)
        {
            service = _navigationService;
        }

        if (service is null)
        {
            // The host is detached: an event snapshot taken before Detach
            // can still deliver here, but with no attached service there
            // is no current page to judge the event against and nothing
            // may be applied to the detached host.
            return;
        }

        if (IsSuperseded(service, regionName, viewModel))
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            ApplyNavigation(viewModel, service, regionName);
        }
        else
        {
            // Re-check at execution time: the ViewModel may have become stale while
            // queued. Also captures the service/region to detect detachment or rename.
            var capturedService = service;
            var capturedRegion = regionName;
            var capturedViewModel = viewModel;
            _dispatcher.Post(() =>
            {
                INavigationService? currentService;
                lock (_eventLock)
                {
                    currentService = _navigationService;
                }

                if (!ReferenceEquals(currentService, capturedService))
                {
                    return;
                }

                if (!string.Equals(capturedRegion, _regionNameProvider(), StringComparison.Ordinal))
                {
                    return;
                }

                if (IsSuperseded(capturedService, capturedRegion, capturedViewModel))
                {
                    return;
                }

                ApplyNavigation(capturedViewModel, capturedService, capturedRegion);
            });
        }
    }

    /// <summary>
    /// Staleness judgment shared by the receipt-time check and the queued
    /// path's execution-time re-check. When the service's current-page
    /// answer is definitive
    /// (<see cref="IDefinitiveCurrentPageQuery"/>), the event is stale
    /// whenever that answer is not the event's ViewModel —
    /// including the answer being null: the page was released while the
    /// event was in flight, and applying it would
    /// resurrect a view for a page the region no longer has. Any other
    /// implementation keeps the compatibility rule: a null answer means
    /// "unknown" and never marks the event stale.
    /// </summary>
    private static bool IsSuperseded(INavigationService? service, string regionName, INavigationViewModel viewModel)
    {
        if (service is IDefinitiveCurrentPageQuery definitive)
        {
            return !ReferenceEquals(definitive.GetDefinitiveCurrentViewModel(regionName), viewModel);
        }

        var current = service?.GetCurrentViewModel(regionName);
        return current is not null && !ReferenceEquals(current, viewModel);
    }

    private void ApplyNavigation(INavigationViewModel viewModel, INavigationService? service, string? regionName)
    {
        if (IsClosed())
        {
            // The host was disposed after this navigation was captured but
            // before it could be applied: no view factory, no failure hook —
            // no user code runs on a disposed host.
            return;
        }

        TView view;
        try
        {
            // Atomic get-or-create: a throwing factory leaves nothing cached, so a
            // later navigation retries creation instead of poisoning the cache.
            view = _viewCache.GetValue(
                    viewModel,
                    key => new StrongBox<TView>(
                        _viewFactory(CreateContext(key, service, regionName)) ?? throw new InvalidOperationException(
                            $"The view factory returned null for ViewModel type '{key.GetType().FullName}'. " +
                            "View factories must not return null.")))
                // Null-forgiving: the box is only constructed with a non-null view (see above).
                .Value!;
        }
        catch (Exception ex)
        {
            // One bad view must not corrupt the host: keep showing the
            // previous content and report the failure through the hook
            // (see ReportViewFailure for the failure-channel design).
            ReportViewFailure(viewModel, ex);
            return;
        }

        if (IsClosed())
        {
            // The host was disposed while this navigation was being applied
            // (a cross-thread Dispose racing an in-flight refresh, property
            // change or dispatcher callback): the created view is dropped
            // instead of being shown on the disposed host.
            return;
        }

        try
        {
            _showView(view, viewModel);
        }
        catch (Exception ex)
        {
            // Showing runs view code — assigning the view's DataContext
            // synchronously runs its initialization — so a show failure
            // gets the creation failure's treatment: the previous
            // content stays (the platform show callbacks assign
            // DataContext before Content, so the throw leaves the old
            // content in place) and the failure is reported through
            // the hook instead of escaping.
            ReportViewFailure(viewModel, ex);
            return;
        }

        _lastShownViewModel = viewModel;
    }

    // The terminal failure channel shared by view creation, showing,
    // and clearing: the failure is reported through the hook exactly
    // once and never rethrown, so one failure cannot surface as three
    // different outcomes — a subscriber error on the synchronous path,
    // an unhandled dispatcher exception on the queued path, or a throw
    // out of a property setter on the refresh path. An exception thrown
    // by the hook itself is swallowed (nowhere further to report). A
    // failure that lands after closure is not reported at all: the
    // hook is user code and must not run on a disposed host.
    private void ReportViewFailure(INavigationViewModel viewModel, Exception exception)
    {
        if (IsClosed())
        {
            return;
        }

        try
        {
            _onViewCreationFailed?.Invoke(viewModel, exception);
        }
        catch
        {
            // Terminal channel: nowhere further to report.
        }
    }

    private ViewCreationContext CreateContext(
        INavigationViewModel viewModel, INavigationService? service, string? regionName)
    {
        // The service and region are the caller's validated snapshot — the
        // same ones the stale checks ran against — never re-read here, so a
        // concurrent re-attach cannot substitute another service's state.
        // The parameter query is keyed by the ViewModel instance, so the
        // parameter it returns belongs to this page even if another
        // navigation has landed in the meantime. No service (detached), or
        // a blank region name (which the queries reject), yields a context
        // without region and parameter.
        if (string.IsNullOrWhiteSpace(regionName))
        {
            return new ViewCreationContext(viewModel, regionName: null, parameter: null);
        }

        var parameter = service?.GetCurrentParameter(viewModel, regionName);
        return new ViewCreationContext(viewModel, regionName, parameter);
    }
}
