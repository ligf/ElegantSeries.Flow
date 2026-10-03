using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ElegantSeries.Flow.Core.Navigation;

namespace ElegantSeries.Flow.Core.ViewModels;

/// <summary>
/// Thin ViewModel base class with no MVVM toolkit dependency.
/// </summary>
/// <remarks>
/// <para>
/// Provides <see cref="INotifyPropertyChanged"/> (with a <see cref="SetProperty{T}"/>
/// helper) and the navigation plumbing shared by all ElegantSeries.Flow ViewModels:
/// the <see cref="Navigation"/> property plus protected navigation helpers.
/// </para>
/// <para>
/// Prefer <c>ElegantSeries.Flow.Mvvm.BaseViewModel</c> (in the optional
/// <c>ElegantSeries.Flow.Mvvm</c> package) when you use CommunityToolkit.Mvvm source
/// generation (<c>[ObservableProperty]</c> and friends); prefer this class when the
/// toolkit dependency is unwanted.
/// </para>
/// <para>
/// The <see cref="Navigation"/> property is managed automatically by the
/// <see cref="NavigationService"/>: it is set when the ViewModel becomes the
/// active page and cleared when it is navigated away from. Application code should
/// call the protected navigation methods instead of touching this property.
/// </para>
/// </remarks>
public abstract class NavigationViewModelBase : INavigationViewModel, INotifyPropertyChanged
{
    /// <summary>
    /// Gets the navigation service attached to this ViewModel, or <see langword="null"/>
    /// if the ViewModel is not currently the active page.
    /// </summary>
    /// <remarks>
    /// Assigned by the navigation service only; application code should call the
    /// protected navigation methods instead of touching this property.
    /// </remarks>
    public INavigationService? Navigation { get; private set; }

    /// <inheritdoc />
    INavigationService? INavigationViewModel.Navigation
    {
        get => Navigation;
        set => Navigation = value;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets the backing field and raises <see cref="PropertyChanged"/> when the value changed.
    /// </summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property name (captured automatically).</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for the given property.
    /// </summary>
    /// <param name="propertyName">The property name (captured automatically).</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Navigates to the specified ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The target ViewModel type.</typeparam>
    /// <param name="regionName">The target navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="mode">The navigation mode. Defaults to <see cref="NavigationMode.New"/>.</param>
    /// <param name="refreshIfActive">
    /// When <see langword="true"/> and the target type is already the active page, re-invokes
    /// its activation callbacks with the new parameter instead of being a silent no-op.
    /// </param>
    /// <param name="cancellationToken">Cooperative cancellation, honored until the region stack is updated.</param>
    /// <returns>
    /// <see langword="true"/> if navigation succeeded;
    /// <see langword="false"/> if it was cancelled or the ViewModel is not active.
    /// </returns>
    protected Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => Navigation?.NavigateToAsync<TViewModel>(regionName, mode, refreshIfActive, cancellationToken)
            ?? Task.FromResult(false);

    /// <summary>
    /// Navigates to the specified ViewModel type with a strongly-typed parameter.
    /// </summary>
    /// <typeparam name="TViewModel">The target ViewModel type.</typeparam>
    /// <typeparam name="TParam">The parameter type.</typeparam>
    /// <param name="parameter">The navigation parameter.</param>
    /// <param name="regionName">The target navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="mode">The navigation mode. Defaults to <see cref="NavigationMode.New"/>.</param>
    /// <param name="refreshIfActive">
    /// When <see langword="true"/> and the target type is already the active page, re-invokes
    /// its activation callbacks with the new parameter instead of being a silent no-op.
    /// </param>
    /// <param name="cancellationToken">Cooperative cancellation, honored until the region stack is updated.</param>
    /// <returns>
    /// <see langword="true"/> if navigation succeeded;
    /// <see langword="false"/> if it was cancelled or the ViewModel is not active.
    /// </returns>
    protected Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        NavigationMode mode = NavigationMode.New,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
        => Navigation?.NavigateToAsync<TViewModel, TParam>(parameter, regionName, mode, refreshIfActive, cancellationToken)
            ?? Task.FromResult(false);

    /// <summary>
    /// Navigates back to the previous page in the specified region.
    /// </summary>
    /// <param name="regionName">The target navigation region. Defaults to <c>"MainRegion"</c>.</param>
    /// <param name="cancellationToken">Cooperative cancellation, honored until the region stack is updated.</param>
    /// <returns>
    /// <see langword="true"/> if back navigation succeeded;
    /// <see langword="false"/> if the stack is empty, the guard cancelled, or the ViewModel is not active.
    /// </returns>
    protected Task<bool> GoBackAsync(string regionName = "MainRegion", CancellationToken cancellationToken = default)
        => Navigation?.GoBackAsync(regionName, cancellationToken) ?? Task.FromResult(false);
}
