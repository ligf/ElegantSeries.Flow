namespace ElegantSeries.Flow.Avalonia;

/// <summary>
/// Minimal dispatcher abstraction used by <see cref="NavigationHost"/> to marshal
/// work onto the UI thread.
/// </summary>
/// <remarks>
/// Abstracted (rather than using <c>Avalonia.Threading.Dispatcher</c> directly) so
/// host behavior can be unit tested without a running Avalonia application.
/// Production code should use <see cref="AvaloniaDispatcher"/>.
/// </remarks>
public interface IDispatcher
{
    /// <summary>
    /// Determines whether the calling thread is the UI thread.
    /// </summary>
    /// <returns><see langword="true"/> if the current thread is the UI thread; otherwise <see langword="false"/>.</returns>
    bool CheckAccess();

    /// <summary>
    /// Queues an action for execution on the UI thread.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    void Post(Action action);
}
