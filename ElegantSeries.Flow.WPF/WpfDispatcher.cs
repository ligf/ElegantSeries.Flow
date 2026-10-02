using System.Windows;
using System.Windows.Threading;

namespace ElegantSeries.Flow.WPF;

/// <summary>
/// <see cref="IDispatcher"/> implementation backed by the WPF <see cref="Dispatcher"/>.
/// </summary>
public sealed class WpfDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance using the current application's dispatcher,
    /// falling back to the calling thread's dispatcher when there is no application
    /// (e.g. in unit tests).
    /// </summary>
    public WpfDispatcher()
        : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
    {
    }

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
        _dispatcher.BeginInvoke(action);
    }
}
