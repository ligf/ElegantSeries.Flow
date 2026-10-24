using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Locating;

/// <summary>
/// Thread-safe ViewModel-type to view-factory registry.
/// </summary>
/// <remarks>
/// <para>
/// This type is generic over the view type and has no UI-framework dependency,
/// which keeps the registration/lookup logic unit-testable without a UI runtime.
/// Each platform's <c>ViewLocator</c> is a thin adapter over <c>ViewRegistry&lt;TView&gt;</c>.
/// </para>
/// <para>
/// Lookups use the ViewModel's exact runtime type (<see cref="object.GetType"/>);
/// no reflection over attributes or assemblies is performed, keeping the
/// registry trimming- and AOT-safe.
/// </para>
/// </remarks>
/// <typeparam name="TView">The view type.</typeparam>
internal sealed class ViewRegistry<TView> where TView : class
{
    private readonly Dictionary<Type, Func<ViewCreationContext, TView>> _factories = new();
    private readonly Lock _syncRoot = new();

    /// <summary>
    /// Registers a view factory for a ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type.</typeparam>
    /// <param name="viewFactory">Creates the view for a ViewModel instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewFactory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A factory is already registered for <typeparamref name="TViewModel"/>.</exception>
    public void Register<TViewModel>(Func<TViewModel, TView> viewFactory)
        where TViewModel : INavigationViewModel
    {
        ArgumentNullException.ThrowIfNull(viewFactory);

        RegisterFactory<TViewModel>(context => viewFactory((TViewModel)context.ViewModel));
    }

    /// <summary>
    /// Registers a context-aware view factory for a ViewModel type. The
    /// factory receives the <see cref="ViewCreationContext"/> of the page the
    /// view is created for (ViewModel, region, activation parameter).
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type.</typeparam>
    /// <param name="viewFactory">Creates the view for a page, given its creation context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewFactory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A factory is already registered for <typeparamref name="TViewModel"/>.</exception>
    public void RegisterFactory<TViewModel>(Func<ViewCreationContext, TView> viewFactory)
        where TViewModel : INavigationViewModel
    {
        ArgumentNullException.ThrowIfNull(viewFactory);

        var key = typeof(TViewModel);
        lock (_syncRoot)
        {
            if (_factories.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"A view is already registered for ViewModel type '{key.FullName}'. " +
                    "Each ViewModel type can only be registered once.");
            }

            _factories[key] = viewFactory;
        }
    }

    /// <summary>
    /// Creates the registered view for a ViewModel instance. The creation
    /// context carries no region and no parameter; use
    /// <see cref="CreateView(ViewCreationContext)"/> to supply them.
    /// </summary>
    /// <param name="viewModel">The ViewModel instance; its exact runtime type is used for the lookup.</param>
    /// <returns>The created view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No view is registered for the ViewModel's type.</exception>
    public TView CreateView(INavigationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        return CreateView(new ViewCreationContext(viewModel, regionName: null, parameter: null));
    }

    /// <summary>
    /// Creates the registered view for the page described by
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The creation context; its ViewModel's exact runtime type is used for the lookup.</param>
    /// <returns>The created view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No view is registered for the ViewModel's type.</exception>
    public TView CreateView(ViewCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var viewModel = context.ViewModel;
        Func<ViewCreationContext, TView>? factory;
        lock (_syncRoot)
        {
            _factories.TryGetValue(viewModel.GetType(), out factory);
        }

        if (factory is null)
        {
            throw new InvalidOperationException(
                $"No view is registered for ViewModel type '{viewModel.GetType().FullName}'. " +
                $"Call {nameof(Register)} first to map the ViewModel type to a view.");
        }

        // User code: exceptions propagate unchanged; a null return fails fast
        // instead of poisoning downstream caches.
        var view = factory(context);
        if (view is null)
        {
            throw new InvalidOperationException(
                $"The view factory registered for ViewModel type '{viewModel.GetType().FullName}' returned null. " +
                "View factories must not return null.");
        }

        return view;
    }

    /// <summary>
    /// Determines whether a view is registered for the given ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to check.</typeparam>
    /// <returns><see langword="true"/> if a view is registered; otherwise <see langword="false"/>.</returns>
    public bool IsRegistered<TViewModel>() where TViewModel : INavigationViewModel
    {
        lock (_syncRoot)
        {
            return _factories.ContainsKey(typeof(TViewModel));
        }
    }
}
