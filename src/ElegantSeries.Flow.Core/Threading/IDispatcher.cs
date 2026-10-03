namespace ElegantSeries.Flow.Core.Threading;

/// <summary>
/// Minimal UI-thread marshaling abstraction. Keeps navigation-host logic
/// unit-testable without a real UI-framework dispatcher.
/// </summary>
/// <remarks>
/// Platform packages expose their own <c>IDispatcher</c> extending this one
/// (for example <c>ElegantSeries.Flow.WPF.Threading.IDispatcher</c>), so
/// existing code referencing the platform types keeps compiling unchanged.
/// </remarks>
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
