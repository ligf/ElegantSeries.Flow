namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// Implement this interface on a ViewModel to intercept navigation away from the current page.
/// </summary>
/// <remarks>
/// <para>
/// The guard is invoked <b>outside</b> the navigation lock to avoid deadlocks,
/// making it safe to display confirmation dialogs or perform async I/O.
/// </para>
/// <para>
/// If the guard returns <see langword="false"/>, the navigation is cancelled and
/// the current page remains active.
/// </para>
/// <para>
/// When the decision depends on where the navigation is going, implement
/// <see cref="INavigationGuardWithContext"/> instead: it is consulted in place of
/// this interface and receives a <see cref="NavigationGuardContext"/>.
/// </para>
/// </remarks>
public interface INavigationGuard
{
    /// <summary>
    /// Determines whether navigation away from the current ViewModel is allowed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> to allow navigation; <see langword="false"/> to cancel it.
    /// </returns>
    Task<bool> CanNavigateFromAsync();
}

/// <summary>
/// Implement this interface on a ViewModel to intercept navigation away from the current page
/// with full context about the pending navigation (target, mode, parameter).
/// </summary>
/// <remarks>
/// <para>
/// When a ViewModel implements this interface, it is consulted <i>instead of</i>
/// <see cref="INavigationGuard"/>: implement only one of the two.
/// </para>
/// <para>
/// Like <see cref="INavigationGuard"/>, the guard is invoked <b>outside</b> the navigation
/// lock to avoid deadlocks, making it safe to display confirmation dialogs or perform
/// async I/O. Returning <see langword="false"/> cancels the navigation and the current
/// page remains active.
/// </para>
/// </remarks>
public interface INavigationGuardWithContext
{
    /// <summary>
    /// Determines whether navigation away from the current ViewModel is allowed.
    /// </summary>
    /// <param name="context">Describes the pending navigation.</param>
    /// <returns>
    /// <see langword="true"/> to allow navigation; <see langword="false"/> to cancel it.
    /// </returns>
    Task<bool> CanNavigateFromAsync(NavigationGuardContext context);
}
