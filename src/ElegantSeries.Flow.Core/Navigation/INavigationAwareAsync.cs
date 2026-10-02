namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// Optional asynchronous variant of <see cref="INavigationAware"/> for ViewModels that
/// need to perform asynchronous work during navigation lifecycle transitions
/// (loading data, opening connections, awaiting confirmation).
/// </summary>
/// <remarks>
/// <para>
/// When a ViewModel implements this interface, the navigation service awaits these
/// callbacks <i>instead of</i> the synchronous <see cref="INavigationAware"/> ones.
/// Implement only one of the two interfaces per ViewModel.
/// </para>
/// <para>
/// The callbacks run outside the navigation locks, in transition order:
/// <c>OnNavigatedFromAsync</c> → page-scope disposal → <c>OnNavigatedToAsync</c> → events.
/// A callback that throws does not abort the transition: the exception is collected and
/// rethrown after the transition completes (a single exception as-is, several wrapped
/// in an <see cref="AggregateException"/>).
/// </para>
/// <para>
/// The <see cref="CancellationToken"/> passed to these callbacks is always
/// <see cref="CancellationToken.None"/>: once the region stack has been updated the
/// transition runs to completion and is no longer cancellable. To cancel a navigation,
/// use the token on <see cref="INavigationService.NavigateToAsync{TViewModel}"/> /
/// <see cref="INavigationService.GoBackAsync"/> or a navigation guard.
/// </para>
/// </remarks>
public interface INavigationAwareAsync
{
    /// <summary>
    /// Called when the ViewModel becomes the active page in its region.
    /// </summary>
    /// <param name="parameter">
    /// The navigation parameter passed by the caller, or <see langword="null"/> if none was provided.
    /// </param>
    /// <param name="cancellationToken">
    /// Always <see cref="CancellationToken.None"/>; reserved for future use.
    /// </param>
    Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the ViewModel is being navigated away from.
    /// </summary>
    /// <param name="cancellationToken">
    /// Always <see cref="CancellationToken.None"/>; reserved for future use.
    /// </param>
    Task OnNavigatedFromAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Strongly-typed variant of <see cref="INavigationAwareAsync"/> that provides
/// compile-time type safety for navigation parameters.
/// </summary>
/// <typeparam name="TParam">The expected type of the navigation parameter.</typeparam>
public interface INavigationAwareAsync<in TParam> : INavigationAwareAsync
{
    /// <summary>
    /// Called when the ViewModel becomes the active page with a strongly-typed parameter.
    /// </summary>
    /// <param name="parameter">The strongly-typed navigation parameter.</param>
    /// <param name="cancellationToken">
    /// Always <see cref="CancellationToken.None"/>; reserved for future use.
    /// </param>
    Task OnNavigatedToAsync(TParam parameter, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    async Task INavigationAwareAsync.OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is TParam typed)
        {
            await OnNavigatedToAsync(typed, cancellationToken).ConfigureAwait(false);
        }
        else if (parameter is null && default(TParam) is null)
        {
            await OnNavigatedToAsync(default!, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new ArgumentException(
                $"Navigation parameter type mismatch: expected {typeof(TParam).Name}, " +
                $"but got {parameter?.GetType().Name ?? "null"}.",
                nameof(parameter));
        }
    }
}
