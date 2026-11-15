using System.Windows;
using System.Windows.Threading;

namespace ElegantSeries.Flow.WPF.Threading;

/// <summary>
/// <see cref="IDispatcher"/> implementation backed by the WPF <see cref="Dispatcher"/>.
/// </summary>
public sealed class WpfDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance using the dispatcher of the thread that
    /// constructs it when that thread already has one — the usual case: a
    /// host created on the UI thread it belongs to, including a secondary
    /// UI thread in a multi-dispatcher application — falling back to the
    /// current application's dispatcher, and then to the calling thread's
    /// dispatcher when there is no application (e.g. in unit tests).
    /// </summary>
    /// <remarks>
    /// The constructing thread wins over the application:
    /// <see cref="Application.Current"/> names the application's main
    /// dispatcher, so preferring it would marshal a host created on a
    /// secondary UI thread to a thread that does not own its elements.
    /// <see cref="Dispatcher.FromThread"/> never creates a dispatcher.
    /// It does return a dispatcher that has already shut down (the
    /// association outlives the loop), and no liveness check is applied:
    /// a host constructed on a thread that no longer pumps cannot
    /// function under any priority.
    /// The final fallback deserves care: <see cref="Dispatcher.CurrentDispatcher"/>
    /// creates a dispatcher for the calling thread when the thread has none,
    /// and a dispatcher only executes posted work while its thread runs a
    /// message loop. On a thread without one (a plain background thread, a
    /// console host), posted actions are queued but never executed — view
    /// switches then silently stall instead of failing. (The Avalonia
    /// counterpart fails fast in the same situation: its default dispatcher
    /// throws when no UI thread is available.) Construct this dispatcher on
    /// the UI thread, or pass an explicit <see cref="Dispatcher"/> whose
    /// thread pumps, whenever there is no running application.
    /// </remarks>
    public WpfDispatcher()
        : this(SelectDispatcher(
            Dispatcher.FromThread(Thread.CurrentThread),
            Application.Current?.Dispatcher,
            static () => Dispatcher.CurrentDispatcher))
    {
    }

    // The default-dispatcher priority, factored out so the ordering is
    // testable without a running Application: the constructing thread's
    // existing dispatcher first, then the application's, then the
    // lazily evaluated (creating) current-thread fallback.
    internal static Dispatcher SelectDispatcher(
        Dispatcher? constructingThreadDispatcher,
        Dispatcher? applicationDispatcher,
        Func<Dispatcher> currentDispatcherFallback)
        => constructingThreadDispatcher ?? applicationDispatcher ?? currentDispatcherFallback();

    /// <summary>
    /// Initializes a new instance using the specified dispatcher.
    /// </summary>
    /// <param name="dispatcher">The dispatcher to marshal calls to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
    public WpfDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        // View switching is user-visible; prioritize above input handling.
        _dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Render);
    }
}
