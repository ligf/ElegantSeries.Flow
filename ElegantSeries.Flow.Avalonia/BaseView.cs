using Avalonia.Controls;
using ElegantSeries.Flow.Core.Navigation;

namespace ElegantSeries.Flow.Avalonia;

/// <summary>
/// Typed base class for Avalonia views bound to a specific ViewModel type.
/// </summary>
/// <typeparam name="TViewModel">The ViewModel type this view displays.</typeparam>
/// <remarks>
/// <para>
/// Derive your view from this class and design it in AXAML as usual. The class
/// must keep a public parameterless constructor (an AXAML/XAML requirement);
/// all services must flow through the ViewModel, which is created by DI.
/// </para>
/// <para>
/// <see cref="NavigationHost"/> assigns the <c>DataContext</c> automatically.
/// </para>
/// </remarks>
public abstract class BaseView<TViewModel> : UserControl
    where TViewModel : class, INavigationViewModel
{
    private TViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseView{TViewModel}"/> class.
    /// </summary>
    protected BaseView()
    {
    }

    /// <summary>
    /// Gets the current ViewModel as its declared type, or <see langword="null"/>
    /// if the <c>DataContext</c> is not (yet) a <typeparamref name="TViewModel"/>.
    /// </summary>
    protected TViewModel? ViewModel => DataContext as TViewModel;

    /// <summary>
    /// Called exactly once per <c>DataContext</c> change that affects the typed
    /// <see cref="ViewModel"/> value.
    /// </summary>
    /// <param name="oldValue">The previous ViewModel, or <see langword="null"/> if there was none.</param>
    /// <param name="newValue">The new ViewModel, or <see langword="null"/> if the <c>DataContext</c> was cleared or set to an incompatible value.</param>
    /// <remarks>
    /// Override this to perform view-side initialization that needs the ViewModel,
    /// such as subscribing to its events. Remember to unsubscribe from
    /// <paramref name="oldValue"/> when it is replaced.
    /// </remarks>
    protected virtual void OnViewModelChanged(TViewModel? oldValue, TViewModel? newValue)
    {
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var newValue = DataContext as TViewModel;
        if (ReferenceEquals(_viewModel, newValue))
        {
            return;
        }

        var oldValue = _viewModel;
        _viewModel = newValue;
        OnViewModelChanged(oldValue, newValue);
    }
}
