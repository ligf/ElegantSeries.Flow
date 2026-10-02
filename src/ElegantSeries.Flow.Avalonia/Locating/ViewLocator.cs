using ElegantSeries.Flow.Core.Navigation;
using Avalonia.Controls;

namespace ElegantSeries.Flow.Avalonia.Locating;

/// <summary>
/// Default <see cref="IViewLocator"/> implementation backed by a dictionary keyed
/// on the ViewModel's exact runtime type.
/// </summary>
/// <remarks>
/// Zero reflection: registration stores a compiled delegate per ViewModel type and
/// lookup uses <see cref="object.GetType"/> only. Safe for Native AOT and trimming.
/// </remarks>
public sealed class ViewLocator : IViewLocator
{
    private readonly Dictionary<Type, Func<INavigationViewModel, Control>> _factories = new();
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public void Register<TView, TViewModel>()
        where TView : Control, new()
        where TViewModel : INavigationViewModel
        => Register<TViewModel>(_ => new TView());

    /// <inheritdoc />
    public void Register<TViewModel>(Func<TViewModel, Control> viewFactory)
        where TViewModel : INavigationViewModel
    {
        ArgumentNullException.ThrowIfNull(viewFactory);

        // The lookup key is the ViewModel's exact runtime type, so this cast is
        // guaranteed to succeed; it only adapts the strongly-typed factory to the
        // weakly-typed dictionary without reflection.
        Func<INavigationViewModel, Control> factory = viewModel => viewFactory((TViewModel)viewModel);

        lock (_lock)
        {
            if (!_factories.TryAdd(typeof(TViewModel), factory))
            {
                throw new InvalidOperationException(
                    $"A view is already registered for ViewModel type '{typeof(TViewModel).FullName}'. " +
                    "Each ViewModel type can only be registered once.");
            }
        }
    }

    /// <inheritdoc />
    public Control CreateView(INavigationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Func<INavigationViewModel, Control>? factory;
        lock (_lock)
        {
            _factories.TryGetValue(viewModel.GetType(), out factory);
        }

        if (factory is null)
        {
            throw new InvalidOperationException(
                $"No view is registered for ViewModel type '{viewModel.GetType().FullName}'. " +
                $"Register one at startup with IViewLocator.Register<TView, {viewModel.GetType().Name}>().");
        }

        // User code: exceptions propagate unchanged; no partial state is kept.
        var view = factory(viewModel);
        if (view is null)
        {
            throw new InvalidOperationException(
                $"The view factory registered for ViewModel type '{viewModel.GetType().FullName}' returned null. " +
                "View factories must return a non-null Control.");
        }

        return view;
    }

    /// <inheritdoc />
    public bool IsRegistered<TViewModel>()
        where TViewModel : INavigationViewModel
    {
        lock (_lock)
        {
            return _factories.ContainsKey(typeof(TViewModel));
        }
    }
}
