using ElegantSeries.Flow.Core.Navigation;

namespace ElegantSeries.Flow.Core.Locating;

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
    private readonly Dictionary<Type, Func<INavigationViewModel, TView>> _factories = new();
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

        var key = typeof(TViewModel);
        lock (_syncRoot)
        {
            if (_factories.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"A view is already registered for ViewModel type '{key.FullName}'. " +
                    "Each ViewModel type can only be registered once.");
            }

            _factories[key] = viewModel => viewFactory((TViewModel)viewModel);
        }
    }

    /// <summary>
    /// Creates the registered view for a ViewModel instance.
    /// </summary>
    /// <param name="viewModel">The ViewModel instance; its exact runtime type is used for the lookup.</param>
    /// <returns>The created view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No view is registered for the ViewModel's type.</exception>
    public TView CreateView(INavigationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Func<INavigationViewModel, TView>? factory;
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
        var view = factory(viewModel);
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
