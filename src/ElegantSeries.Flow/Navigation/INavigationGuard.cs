namespace ElegantSeries.Flow.Navigation;

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
/// If the guard throws an exception, the exception propagates to the navigation
/// caller and the navigation is aborted. Guard implementations should return
/// <see langword="false"/> for expected vetoes and only throw for programming errors.
/// </para>
/// <para>
/// When the decision depends on where the navigation is going, implement
/// <see cref="INavigationGuardWithContext"/> instead: it is consulted in place of
/// this interface and receives a <see cref="NavigationGuardContext"/>.
/// </para>
/// <para>
/// The guard runs once per departure, against the request that started the
/// departure. A navigation request registered later — from
/// <see cref="INavigationAware.OnNavigatedFrom"/> or from the guard itself —
/// supersedes the pending request but does not re-run the guard.
/// </para>
/// <para>
/// The guard may wait indefinitely — for example on user confirmation — and
/// the navigation service imposes no timeout on it. While a guard is
/// running, the region's navigation is blocked: a later request supersedes
/// the pending one but does not interrupt the running guard. The region
/// unblocks when the guard returns, when the request that started the
/// departure is cancelled, or when the service is disposed. A guard that
/// waits on external events should therefore observe the supplied
/// cancellation token and apply its own timeout where one is needed.
/// </para>
/// </remarks>
public interface INavigationGuard
{
    /// <summary>
    /// Determines whether navigation away from the current ViewModel is allowed.
    /// </summary>
    /// <param name="cancellationToken">
    /// Signals that the navigation request was cancelled while the guard was running.
    /// The guard should stop promptly when signalled; the navigation service stops
    /// waiting for the guard once this token fires, but the guard task itself cannot
    /// be force-aborted, so cooperative cancellation is the guard's responsibility.
    /// </param>
    /// <returns>
    /// <see langword="true"/> to allow navigation; <see langword="false"/> to cancel it.
    /// </returns>
    Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken);
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
/// page remains active. A thrown exception propagates to the navigation caller.
/// </para>
/// <para>
/// Also like <see cref="INavigationGuard"/>, the guard may wait indefinitely:
/// the service imposes no timeout, and a later request supersedes the pending
/// one without interrupting the running guard. The region unblocks when the
/// guard returns, when the starting request is cancelled, or when the
/// service is disposed.
/// </para>
/// </remarks>
public interface INavigationGuardWithContext
{
    /// <summary>
    /// Determines whether navigation away from the current ViewModel is allowed.
    /// </summary>
    /// <param name="context">Describes the pending navigation.</param>
    /// <param name="cancellationToken">
    /// Signals that the navigation request was cancelled while the guard was running.
    /// The guard should stop promptly when signalled; the navigation service stops
    /// waiting for the guard once this token fires, but the guard task itself cannot
    /// be force-aborted, so cooperative cancellation is the guard's responsibility.
    /// </param>
    /// <returns>
    /// <see langword="true"/> to allow navigation; <see langword="false"/> to cancel it.
    /// </returns>
    Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken);
}
