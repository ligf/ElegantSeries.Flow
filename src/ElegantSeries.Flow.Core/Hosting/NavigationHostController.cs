using System.Runtime.CompilerServices;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.Threading;

namespace ElegantSeries.Flow.Core.Hosting;

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
/// misconfigured host, or a throwing factory). Failures never corrupt the
/// currently shown content; they are reported through the
/// <c>onViewCreationFailed</c> hook. They are deliberately not rethrown: the
/// navigation service isolates event subscribers and swallows their exceptions
/// by design, so throwing could never fail fast — the hook is the observable channel.
/// </para>
/// <para>
/// Threading: <see cref="Attach(INavigationService)"/>/<see cref="Detach"/> are
/// serialized; the navigation callback is filtered and then marshaled to the UI
/// thread through <see cref="IDispatcher"/>. No lock is held while user code
/// (the view factory or the show callback) runs.
/// </para>
/// </remarks>
/// <typeparam name="TView">The view type.</typeparam>
internal sealed class NavigationHostController<TView> where TView : class
{
    private readonly IDispatcher _dispatcher;
    private readonly Func<string> _regionNameProvider;
    private readonly Func<INavigationViewModel, TView> _viewFactory;
    private readonly Action<TView, INavigationViewModel> _showView;
    private readonly Action _clearView;
    private readonly Action<INavigationViewModel, Exception>? _onViewCreationFailed;
    // StrongBox works around a trim-analysis papercut: ConditionalWeakTable<TKey, TValue>
    // demands a public parameterless constructor on TValue (for GetOrCreateValue),
    // which we never call. Boxing the view keeps TView free of constructor constraints.
    private readonly ConditionalWeakTable<INavigationViewModel, StrongBox<TView>> _viewCache = new();
    private readonly Lock _eventLock = new();
    private INavigationService? _navigationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationHostController{TView}"/> class.
    /// </summary>
    /// <param name="dispatcher">Marshals view updates to the UI thread.</param>
    /// <param name="regionNameProvider">Returns the region this host displays. Evaluated per event so renames take effect immediately.</param>
    /// <param name="viewFactory">
    /// Creates the view for a ViewModel. May throw (for example
    /// <see cref="InvalidOperationException"/> for an unregistered ViewModel type);
    /// failures are reported via <paramref name="onViewCreationFailed"/> and the
    /// previously shown content is kept.
    /// </param>
    /// <param name="showView">Displays a view for a ViewModel. Always runs on the UI thread.</param>
    /// <param name="clearView">Clears the displayed content. Always runs on the UI thread.</param>
    /// <param name="onViewCreationFailed">Optional hook invoked when <paramref name="viewFactory"/> throws.</param>
    public NavigationHostController(
        IDispatcher dispatcher,
        Func<string> regionNameProvider,
        Func<INavigationViewModel, TView> viewFactory,
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
    /// event, replacing any previous subscription.
    /// </summary>
    /// <param name="navigationService">The navigation service to observe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="navigationService"/> is <see langword="null"/>.</exception>
    public void Attach(INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(navigationService);

        lock (_eventLock)
        {
            DetachLocked();
            _navigationService = navigationService;
            navigationService.RegionNavigated += OnRegionNavigated;
        }
    }

    /// <summary>
    /// Unsubscribes from the navigation service. Safe to call multiple times.
    /// </summary>
    public void Detach()
    {
        lock (_eventLock)
        {
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
            _clearView();
            return;
        }

        var viewModel = service.GetCurrentViewModel(regionName);
        if (viewModel is null)
        {
            _clearView();
            return;
        }

        ApplyNavigation(viewModel);
    }

    private void DetachLocked()
    {
        if (_navigationService is not null)
        {
            _navigationService.RegionNavigated -= OnRegionNavigated;
            _navigationService = null;
        }
    }

    private void OnRegionNavigated(string regionName, INavigationViewModel viewModel)
    {
        if (!string.Equals(regionName, _regionNameProvider(), StringComparison.Ordinal))
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            ApplyNavigation(viewModel);
        }
        else
        {
            _dispatcher.Post(() => ApplyNavigation(viewModel));
        }
    }

    private void ApplyNavigation(INavigationViewModel viewModel)
    {
        TView view;
        try
        {
            // Atomic get-or-create: a throwing factory leaves nothing cached, so a
            // later navigation retries creation instead of poisoning the cache.
            view = _viewCache.GetValue(
                    viewModel,
                    key => new StrongBox<TView>(
                        _viewFactory(key) ?? throw new InvalidOperationException(
                            $"The view factory returned null for ViewModel type '{key.GetType().FullName}'. " +
                            "View factories must not return null.")))
                // Null-forgiving: the box is only constructed with a non-null view (see above).
                .Value!;
        }
        catch (Exception ex)
        {
            // One bad view must not corrupt the host: keep showing the previous
            // content and report the failure through the hook.
            _onViewCreationFailed?.Invoke(viewModel, ex);
            return;
        }

        _showView(view, viewModel);
    }
}
