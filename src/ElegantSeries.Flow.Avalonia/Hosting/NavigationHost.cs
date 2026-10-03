using Avalonia;
using Avalonia.Controls;
using ElegantSeries.Flow.Core.Hosting;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Avalonia.Threading;

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
/// Region filtering, UI-thread marshaling, view caching, and failure handling are
/// implemented by the shared <c>NavigationHostController&lt;Control&gt;</c> in
/// <c>ElegantSeries.Flow.Core</c> (the same logic the WPF host uses); this class
/// only adapts it to Avalonia's property system and visual-tree lifetime.
/// </para>
/// <para>
/// Threading: the navigation service may raise <c>RegionNavigated</c> on any thread
/// (library code uses <c>ConfigureAwait(false)</c>). All UI work is marshalled to the
/// UI thread through <see cref="IDispatcher"/>; view creation therefore always happens
/// on the UI thread. The default constructor uses <see cref="AvaloniaDispatcher"/>.
/// </para>
/// <para>
/// View caching: views are cached per ViewModel instance with a weak reference to
/// the ViewModel, so a view's lifetime is bound to its ViewModel's. A KeepAlive
/// ViewModel automatically reuses its view; once a non-KeepAlive ViewModel is
/// released by the navigation service, its view becomes eligible for garbage
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

    private readonly NavigationHostController<Control> _controller;
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

        _controller = new NavigationHostController<Control>(
            dispatcher,
            regionNameProvider: () => _regionName,
            viewFactory: CreateView,
            showView: (view, viewModel) =>
            {
                view.DataContext = viewModel;
                Content = view;
            },
            onViewCreationFailed: (viewModel, exception) => OnViewCreationFailed(viewModel, exception));
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
        _controller.Detach();
        _subscribedService = null;
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
        _controller.Detach();
        _subscribedService = null;
    }

    private Control CreateView(INavigationViewModel viewModel)
    {
        if (ViewLocator is null)
        {
            throw new InvalidOperationException(
                $"NavigationHost for region '{_regionName}' cannot display a page because {nameof(ViewLocator)} is not set. " +
                $"Assign an {nameof(IViewLocator)} before navigation occurs.");
        }

        return ViewLocator.CreateView(viewModel);
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

        _subscribedService = service;
        if (service is not null)
        {
            // Attach replaces any previous subscription inside the controller.
            _controller.Attach(service);
        }
        else
        {
            _controller.Detach();
        }
    }
}
