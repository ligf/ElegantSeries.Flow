using System.Windows;
using System.Windows.Controls;
using ElegantSeries.Flow.Core.Hosting;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using ElegantSeries.Flow.WPF.Threading;

namespace ElegantSeries.Flow.WPF.Hosting;

/// <summary>
/// A <see cref="ContentControl"/> that displays the active view for a navigation region.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="RegionName"/>, <see cref="NavigationService"/>, and
/// <see cref="ViewLocator"/> (in XAML or code), then navigate with the core
/// <see cref="INavigationService"/>: the host listens to
/// <see cref="INavigationService.RegionNavigated"/>, creates the registered view
/// via the <see cref="ViewLocator"/>, assigns the ViewModel as
/// <see cref="FrameworkElement.DataContext"/>, and shows it as <see cref="ContentControl.Content"/>.
/// </para>
/// <para>
/// The navigation service may raise events on a thread-pool thread; all UI work
/// is marshaled to the UI thread automatically.
/// </para>
/// <para>
/// View caching: views are cached per ViewModel instance with a weak reference to
/// the ViewModel, so KeepAlive navigation reuses the existing view for free and
/// non-KeepAlive views become collectible together with their ViewModel. No
/// manual cache cleanup is needed.
/// </para>
/// <para>
/// Failure handling: if view creation fails (the <see cref="ViewLocator"/> is not
/// set, the ViewModel type is not registered, or the view factory throws), the host
/// keeps showing the previous content and calls <see cref="OnViewCreationFailed"/>.
/// Override it to log or surface the error.
/// </para>
/// <para>
/// Lifetime: the navigation service is typically a singleton while the host is a
/// view. Call <see cref="Dispose"/> (e.g. when the containing window closes) to
/// unsubscribe from the service; otherwise the event subscription keeps the host
/// alive.
/// </para>
/// </remarks>
public class NavigationHost : ContentControl, IDisposable
{
    /// <summary>
    /// Identifies the <see cref="RegionName"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty RegionNameProperty =
        DependencyProperty.Register(
            nameof(RegionName),
            typeof(string),
            typeof(NavigationHost),
            new PropertyMetadata("MainRegion", OnRegionNameChanged));

    /// <summary>
    /// Identifies the <see cref="NavigationService"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty NavigationServiceProperty =
        DependencyProperty.Register(
            nameof(NavigationService),
            typeof(INavigationService),
            typeof(NavigationHost),
            new PropertyMetadata(null, OnNavigationServiceChanged));

    /// <summary>
    /// Identifies the <see cref="ViewLocator"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ViewLocatorProperty =
        DependencyProperty.Register(
            nameof(ViewLocator),
            typeof(IViewLocator),
            typeof(NavigationHost),
            new PropertyMetadata(null, OnViewLocatorChanged));

    private readonly NavigationHostController<FrameworkElement> _controller;
    private bool _disposed;

    // Mirrors RegionNameProperty in a plain field: the navigation service may raise
    // events on a thread-pool thread, and DependencyObject.GetValue must only be
    // called on the UI thread. The callback below runs on the UI thread; volatile
    // keeps the write visible to event threads.
    private volatile string _regionName = "MainRegion";

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationHost"/> class
    /// using the WPF dispatcher.
    /// </summary>
    public NavigationHost()
        : this(new WpfDispatcher())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationHost"/> class
    /// using the specified dispatcher. For subclasses (e.g. tests).
    /// </summary>
    /// <param name="dispatcher">The dispatcher used to marshal UI updates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
    protected NavigationHost(Core.Threading.IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        _controller = new NavigationHostController<FrameworkElement>(
            dispatcher,
            regionNameProvider: () => _regionName,
            viewFactory: CreateView,
            showView: (view, viewModel) =>
            {
                view.DataContext = viewModel;
                Content = view;
            },
            clearView: () => Content = null,
            onViewCreationFailed: (viewModel, exception) => OnViewCreationFailed(viewModel, exception));
    }

    /// <summary>
    /// Gets or sets the navigation region this host displays. Defaults to <c>"MainRegion"</c>.
    /// Only <see cref="INavigationService.RegionNavigated"/> events for this region are handled.
    /// Changing the value immediately re-displays the new region's current page
    /// (or clears the host when the region is empty), so it can be data-bound.
    /// </summary>
    public string RegionName
    {
        get => (string?)GetValue(RegionNameProperty) ?? "MainRegion";
        set => SetValue(RegionNameProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// Gets or sets the navigation service to observe. Setting a new service
    /// unsubscribes from the previous one; setting <see langword="null"/> detaches.
    /// This is a dependency property, so it can be data-bound.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    public INavigationService? NavigationService
    {
        get => (INavigationService?)GetValue(NavigationServiceProperty);
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SetValue(NavigationServiceProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the view locator used to create views for navigated ViewModels.
    /// This is a dependency property, so it can be data-bound.
    /// </summary>
    /// <remarks>
    /// Must be set before navigation occurs. If a navigation event arrives while
    /// this is <see langword="null"/>, the failure is reported through
    /// <see cref="OnViewCreationFailed"/> as an <see cref="InvalidOperationException"/>.
    /// (It cannot be thrown to the caller: the navigation service isolates event
    /// subscribers and swallows their exceptions by design, so the hook is the
    /// observable channel.)
    /// </remarks>
    public IViewLocator? ViewLocator
    {
        get => (IViewLocator?)GetValue(ViewLocatorProperty);
        set => SetValue(ViewLocatorProperty, value);
    }

    /// <summary>
    /// Called when view creation fails for a navigated ViewModel: the ViewModel
    /// type is not registered, or the view factory threw. The host keeps showing
    /// the previous content.
    /// </summary>
    /// <param name="viewModel">The ViewModel no view could be created for.</param>
    /// <param name="exception">
    /// The failure. An <see cref="InvalidOperationException"/> whose message names
    /// <see cref="ViewLocator"/> indicates missing host configuration; any other
    /// exception comes from the view factory itself.
    /// </param>
    /// <remarks>
    /// Runs on the UI thread. The default implementation does nothing. Override to
    /// log the error, and rethrow during development to fail fast on misconfiguration.
    /// </remarks>
    protected virtual void OnViewCreationFailed(INavigationViewModel viewModel, Exception exception)
    {
    }

    /// <summary>
    /// Unsubscribes from the navigation service. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Detach();
        // Bypass the CLR setter (which throws once disposed): the guarded
        // callback ignores post-dispose changes, so this only drops the reference.
        SetValue(NavigationServiceProperty, null);
    }

    private FrameworkElement CreateView(INavigationViewModel viewModel)
    {
        if (ViewLocator is null)
        {
            throw new InvalidOperationException(
                $"NavigationHost (region '{_regionName}') cannot display views because {nameof(ViewLocator)} is not set. " +
                $"Assign an {nameof(IViewLocator)} (for example via services.AddFlowViews()) before navigating.");
        }

        return ViewLocator.CreateView(viewModel);
    }

    private static void OnRegionNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Runs on the UI thread (dependency properties have thread affinity).
        var host = (NavigationHost)d;
        host._regionName = (string?)e.NewValue ?? "MainRegion";
        host._controller.Refresh();
    }

    private static void OnNavigationServiceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Runs on the UI thread (dependency properties have thread affinity).
        // Post-dispose changes (e.g. a stale binding update, or Dispose itself
        // clearing the property) are ignored; explicit code-behind sets after
        // dispose still fail fast via the CLR setter's ObjectDisposedException.
        var host = (NavigationHost)d;
        if (host._disposed)
        {
            return;
        }

        host._controller.Detach();
        if (e.NewValue is INavigationService service)
        {
            host._controller.Attach(service);
        }

        // The host reflects its region: show the current page immediately
        // instead of waiting for the next navigation event.
        host._controller.Refresh();
    }

    private static void OnViewLocatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Runs on the UI thread (dependency properties have thread affinity).
        // Views can now be created (or can no longer be created): re-display
        // the region's current page.
        ((NavigationHost)d)._controller.Refresh();
    }
}
