namespace ElegantSeries.Flow.WPF.Threading;

/// <summary>
/// Minimal UI-thread marshaling abstraction. Keeps navigation-host logic
/// unit-testable without a real WPF <see cref="System.Windows.Threading.Dispatcher"/>.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Determines whether the calling thread is the UI thread.
    /// </summary>
    /// <returns><see langword="true"/> if the calling thread can access UI objects directly.</returns>
    bool CheckAccess();

    /// <summary>
    /// Queues an action for execution on the UI thread (fire-and-forget).
    /// </summary>
    /// <param name="action">The action to run on the UI thread.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    void Post(Action action);
}
