using Avalonia;
using Avalonia.Controls;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace ElegantSeries.Flow.Avalonia.Hosting;

/// <summary>
/// A <see cref="ContentControl"/> that displays the active page of a navigation region.
/// </summary>
/// <remarks>
/// <para>
/// The host subscribes to <see cref="INavigationService.RegionNavigated"/> and, when the
/// event's region matches <see cref="RegionName"/>, resolves (or reuses) the view for the
/// activated ViewModel via <see cref="IViewLocator"/>, assigns it as <c>DataContext</c>,
/// and shows it as <see cref="ContentControl.Content"/>.
/// </para>
/// <para>
/// Threading: the navigation service may raise <c>RegionNavigated</c> on any thread
/// (library code uses <c>ConfigureAwait(false)</c>). All UI work is marshalled to the
/// UI thread through <see cref="IDispatcher"/>; view creation therefore always happens
/// on the UI thread. The default constructor uses <see cref="AvaloniaDispatcher"/>.
/// </para>
/// <para>
/// View caching: views are cached in a <see cref="ConditionalWeakTable{TKey, TValue}"/>
/// keyed by ViewModel instance, so a view's lifetime is bound to its ViewModel's.
/// A KeepAlive ViewModel automatically reuses its view; once a non-KeepAlive ViewModel
/// is released by the navigation service, its view becomes eligible for garbage
/// collection with no manual cleanup.
/// </para>
/// <para>
/// Failure handling: if view creation fails (the <see cref="ViewLocator"/> is not
/// set, the ViewModel type is not registered, or the view factory throws), the host
/// keeps showing the previous content and calls <see cref="OnViewCreationFailed"/>.
/// Override it to log or surface the error. Failures are deliberately not thrown:
/// the navigation service isolates event subscribers and swallows their exceptions
/// by design, and an exception escaping a posted UI-thread callback would tear
/// down the application's message loop — so the hook is the observable channel.
/// </para>
/// <para>
/// Lifetime: the navigation service is typically a singleton while the host is a view.
/// The host unsubscribes from <c>RegionNavigated</c> when it is detached from the visual
/// tree and when <see cref="Dispose"/> is called, so a discarded host is never kept alive
/// by the service's event.
/// </para>
/// </remarks>
public class NavigationHost : ContentControl, IDisposable
{
    /// <summary>
    /// Defines the <see cref="RegionName"/> property.
    /// </summary>
    public static readonly StyledProperty<string> RegionNameProperty =
        AvaloniaProperty.Register<NavigationHost, string>(nameof(RegionName), defaultValue: "MainRegion");

    /// <summary>
    /// Defines the <see cref="NavigationService"/> property.
    /// </summary>
    public static readonly StyledProperty<INavigationService?> NavigationServiceProperty =
        AvaloniaProperty.Register<NavigationHost, INavigationService?>(nameof(NavigationService));

    /// <summary>
    /// Defines the <see cref="ViewLocator"/> property.
    /// </summary>
    public static readonly StyledProperty<IViewLocator?> ViewLocatorProperty =
        AvaloniaProperty.Register<NavigationHost, IViewLocator?>(nameof(ViewLocator));

    private readonly IDispatcher _dispatcher;
    private readonly ConditionalWeakTable<INavigationViewModel, Control> _viewCache = new();
    private INavigationService? _subscribedService;
    private bool _disposed;

    // Mirrors RegionNameProperty in a plain field: the navigation service may raise
    // RegionNavigated on a thread-pool thread, and GetValue(StyledProperty<T>) calls
    // VerifyAccess(), which throws InvalidOperationException off the UI thread (the
    // navigation service swallows subscriber exceptions, so the host would silently
    // stop updating). OnPropertyChanged runs on the UI thread; volatile keeps the
    // write visible to event threads.
    private volatile string _regionName = "MainRegion";

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationHost"/> class using the
    /// Avalonia UI thread dispatcher.
    /// </summary>
    /// <remarks>
    /// This is the constructor used by AXAML. It requires a running Avalonia application;
    /// in unit tests, use <see cref="NavigationHost(IDispatcher)"/> with a test double.
    /// </remarks>
    public NavigationHost()
        : this(new AvaloniaDispatcher())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationHost"/> class with an
    /// explicit dispatcher.
    /// </summary>
    /// <param name="dispatcher">The dispatcher used to marshal UI work.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
    public NavigationHost(IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Gets or sets the name of the navigation region this host displays.
    /// Only <c>RegionNavigated</c> events whose region matches (ordinal comparison)
    /// are processed. Defaults to <c>"MainRegion"</c>.
    /// </summary>
    public string RegionName
    {
        get => GetValue(RegionNameProperty);
        set => SetValue(RegionNameProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// Gets or sets the navigation service to observe. Changing the value
    /// unsubscribes the previous service and subscribes the new one.
    /// </summary>
    public INavigationService? NavigationService
    {
        get => GetValue(NavigationServiceProperty);
        set => SetValue(NavigationServiceProperty, value);
    }

    /// <summary>
    /// Gets or sets the view locator used to create views for activated ViewModels.
    /// </summary>
    /// <remarks>
    /// If a navigation arrives while this is <see langword="null"/>, the failure is
    /// reported through <see cref="OnViewCreationFailed"/> as an
    /// <see cref="InvalidOperationException"/> (it cannot be thrown to the caller:
    /// the navigation service isolates event subscribers and swallows their
    /// exceptions by design, so the hook is the observable channel).
    /// </remarks>
    public IViewLocator? ViewLocator
    {
        get => GetValue(ViewLocatorProperty);
        set => SetValue(ViewLocatorProperty, value);
    }

    /// <summary>
    /// Releases the event subscription held on the navigation service.
    /// After disposal the host ignores further navigation events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unsubscribe();
    }

    /// <summary>
    /// Called when view creation fails for a navigated ViewModel: the
    /// <see cref="ViewLocator"/> is not set, the ViewModel type is not registered,
    /// or the view factory threw. The host keeps showing the previous content.
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

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == NavigationServiceProperty)
        {
            UpdateSubscription();
        }

        if (change.Property == RegionNameProperty)
        {
            // Runs on the UI thread (SetValue verifies access); mirrors the value
            // into _regionName so background event threads never touch the
            // property system.
            _regionName = change.GetNewValue<string>() ?? "MainRegion";
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateSubscription();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // The host may be discarded while the (singleton) service lives on;
        // dropping the subscription here prevents the service from keeping the host alive.
        Unsubscribe();
    }

    private void UpdateSubscription()
    {
        if (_disposed)
        {
            return;
        }

        var service = NavigationService;
        if (ReferenceEquals(service, _subscribedService))
        {
            return;
        }

        Unsubscribe();

        if (service is not null)
        {
            service.RegionNavigated += OnRegionNavigated;
            _subscribedService = service;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedService is not null)
        {
            _subscribedService.RegionNavigated -= OnRegionNavigated;
            _subscribedService = null;
        }
    }

    private void OnRegionNavigated(string regionName, INavigationViewModel viewModel)
    {
        // The navigation service may raise this on any thread: compare against the
        // mirrored _regionName field, never the StyledProperty (see the field docs).
        if (!string.Equals(regionName, _regionName, StringComparison.Ordinal))
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

    /// <summary>
    /// Switches the displayed content to the view for <paramref name="viewModel"/>.
    /// Must be called on the UI thread.
    /// </summary>
    /// <remarks>
    /// <see cref="ContentControl.Content"/> is assigned only after the view was
    /// successfully created and bound, so a throwing view factory can never leave
    /// the host in a half-updated state: the previous content stays visible and the
    /// failure is reported through <see cref="OnViewCreationFailed"/>.
    /// </remarks>
    private void ApplyNavigation(INavigationViewModel viewModel)
    {
        Control view;
        try
        {
            var locator = ViewLocator
                ?? throw new InvalidOperationException(
                    $"NavigationHost for region '{RegionName}' cannot display a page because {nameof(ViewLocator)} is not set. " +
                    $"Assign an {nameof(IViewLocator)} before navigation occurs.");

            if (!_viewCache.TryGetValue(viewModel, out var cached))
            {
                // User code (view factory) may throw: nothing is cached below,
                // so the host keeps showing the previous page and a later
                // navigation retries creation instead of poisoning the cache.
                cached = locator.CreateView(viewModel);
                cached.DataContext = viewModel;
                _viewCache.Add(viewModel, cached);
            }

            view = cached;
        }
        catch (Exception ex)
        {
            // One bad view must not corrupt the host: keep showing the previous
            // content and report the failure through the hook (see remarks above
            // for why it is not rethrown).
            OnViewCreationFailed(viewModel, ex);
            return;
        }

        Content = view;
    }
}
