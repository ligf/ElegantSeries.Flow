using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ElegantSeries.Flow.Navigation;

using static ElegantSeries.Flow.Navigation.NavigationDefaults;

namespace ElegantSeries.Flow.ViewModels;

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
    private INavigationService? _navigation;

    /// <summary>
    /// Gets the navigation service attached to this ViewModel, or <see langword="null"/>
    /// if the ViewModel is not currently the active page.
    /// </summary>
    /// <remarks>
    /// Assigned by the navigation service only; application code should call the
    /// protected navigation methods instead of touching this property.
    /// Raises <see cref="PropertyChanged"/> when changed, so it can be data-bound
    /// (e.g. <c>NavigationService="{Binding Navigation}"</c> on a
    /// <c>NavigationHost</c>).
    /// </remarks>
    public INavigationService? Navigation
    {
        get => _navigation;
        private set => SetProperty(ref _navigation, value);
    }

    /// <inheritdoc />
    INavigationService? INavigationViewModel.Navigation
    {
        get => Navigation;
        set => Navigation = value;
    }

    /// <inheritdoc />
    void INavigationViewModel.SetNavigationSilent(INavigationService? service)
    {
        _navigation = service;
    }

    /// <inheritdoc />
    void INavigationViewModel.RaiseNavigationChanged()
    {
        OnPropertyChanged(nameof(Navigation));
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
    /// <param name="cancellationToken">Cooperative cancellation, passed through to the navigation service.</param>
    /// <returns>
    /// The fate of the request as defined by the navigation service:
    /// <see langword="true"/> if it was executed; <see langword="false"/> if it
    /// was superseded, vetoed by the leave guard, or discarded on disposal —
    /// or when this ViewModel is not attached to a navigation service.
    /// When called from within a lifecycle callback, the task is already
    /// completed — with <see langword="true"/> when the request was
    /// registered, or with <see langword="false"/> when the service has
    /// already been disposed and nothing was registered — and it does not
    /// report the request's later fate. Cancelling
    /// <paramref name="cancellationToken"/> does not return
    /// <see langword="false"/>: the returned task completes as cancelled.
    /// </returns>
    protected Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = DefaultRegionName,
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
    /// <param name="cancellationToken">Cooperative cancellation, passed through to the navigation service.</param>
    /// <returns>
    /// The fate of the request as defined by the navigation service:
    /// <see langword="true"/> if it was executed; <see langword="false"/> if it
    /// was superseded, vetoed by the leave guard, or discarded on disposal —
    /// or when this ViewModel is not attached to a navigation service.
    /// When called from within a lifecycle callback, the task is already
    /// completed — with <see langword="true"/> when the request was
    /// registered, or with <see langword="false"/> when the service has
    /// already been disposed and nothing was registered — and it does not
    /// report the request's later fate. Cancelling
    /// <paramref name="cancellationToken"/> does not return
    /// <see langword="false"/>: the returned task completes as cancelled.
    /// </returns>
    protected Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = DefaultRegionName,
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
    /// <param name="cancellationToken">Cooperative cancellation, passed through to the navigation service.</param>
    /// <returns>
    /// The fate of the request as defined by the navigation service:
    /// <see langword="true"/> if it was executed; <see langword="false"/> if it
    /// was superseded, vetoed by the leave guard, the stack has one or fewer
    /// pages, or the service was disposed — or when this ViewModel is not
    /// attached to a navigation service. When called from within a lifecycle
    /// callback, the task is already completed — with <see langword="true"/>
    /// when the request was registered, or with <see langword="false"/> when
    /// the service has already been disposed and nothing was registered —
    /// and it does not report the request's later fate. Cancelling <paramref name="cancellationToken"/> does not return
    /// <see langword="false"/>: the returned task completes as cancelled.
    /// </returns>
    protected Task<bool> GoBackAsync(string regionName = DefaultRegionName, CancellationToken cancellationToken = default)
        => Navigation?.GoBackAsync(regionName, cancellationToken) ?? Task.FromResult(false);
}
