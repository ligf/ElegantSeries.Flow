using Avalonia.Threading;

namespace ElegantSeries.Flow.Avalonia.Threading;

/// <summary>
/// <see cref="IDispatcher"/> implementation backed by Avalonia's UI thread dispatcher.
/// </summary>
public sealed class AvaloniaDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance targeting the Avalonia UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no Avalonia UI thread is available (e.g. no <c>Application</c> is running).
    /// </exception>
    public AvaloniaDispatcher()
        : this(Dispatcher.UIThread ?? throw new InvalidOperationException(
            "No Avalonia UI thread is available. Ensure an Avalonia Application is running before creating an AvaloniaDispatcher."))
    {
    }

    /// <summary>
    /// Initializes a new instance targeting the given dispatcher.
    /// </summary>
    /// <param name="dispatcher">The dispatcher to marshal work to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
    public AvaloniaDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher.Post(action);
    }
}
