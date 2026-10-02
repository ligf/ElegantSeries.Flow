using System.Windows;
using System.Windows.Controls;
using ElegantSeries.Flow.Core.Navigation;

namespace ElegantSeries.Flow.WPF.Views;

/// <summary>
/// Base class for WPF views bound to a strongly-typed ViewModel.
/// </summary>
/// <remarks>
/// <para>
/// Derive your XAML view's code-behind from this class instead of
/// <see cref="UserControl"/>:
/// <code>
/// public partial class OrderView : BaseView&lt;OrderViewModel&gt;
/// {
///     public OrderView() =&gt; InitializeComponent();
/// }
/// </code>
/// </para>
/// <para>
/// The constructor must stay parameterless (a XAML requirement); all
/// dependencies flow through the ViewModel, which is created by DI.
/// <see cref="Hosting.NavigationHost"/> sets <see cref="FrameworkElement.DataContext"/>
/// to the active ViewModel.
/// </para>
/// </remarks>
/// <typeparam name="TViewModel">The ViewModel type this view displays.</typeparam>
public abstract class BaseView<TViewModel> : UserControl
    where TViewModel : class, INavigationViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BaseView{TViewModel}"/> class.
    /// </summary>
    protected BaseView()
    {
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Gets the typed ViewModel from <see cref="FrameworkElement.DataContext"/>,
    /// or <see langword="null"/> if it is not set or has an unexpected type.
    /// </summary>
    protected TViewModel? ViewModel => DataContext as TViewModel;

    /// <summary>
    /// Called exactly once when the typed ViewModel changes (including transitions
    /// to or from <see langword="null"/>). Override to perform view-side
    /// initialization, such as subscribing to ViewModel events.
    /// </summary>
    /// <param name="oldValue">The previous ViewModel, or <see langword="null"/>.</param>
    /// <param name="newValue">The new ViewModel, or <see langword="null"/>.</param>
    /// <remarks>
    /// Runs on the UI thread. Unsubscribe anything you subscribe to here when
    /// <paramref name="newValue"/> is <see langword="null"/> or when a different
    /// ViewModel replaces the current one.
    /// </remarks>
    protected virtual void OnViewModelChanged(TViewModel? oldValue, TViewModel? newValue)
    {
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var oldValue = e.OldValue as TViewModel;
        var newValue = e.NewValue as TViewModel;

        // DataContext is a DependencyProperty: WPF already suppresses notifications
        // when the value does not change. The reference check below guards the
        // remaining edge cases (e.g. a non-TViewModel value replaced by null).
        if (ReferenceEquals(oldValue, newValue))
        {
            return;
        }

        OnViewModelChanged(oldValue, newValue);
    }
}
