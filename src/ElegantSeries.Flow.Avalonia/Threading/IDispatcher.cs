namespace ElegantSeries.Flow.Avalonia.Threading;

/// <summary>
/// Avalonia UI-thread marshaling abstraction. Extends
/// <see cref="Core.Threading.IDispatcher"/> so dispatchers also satisfy the
/// shared host logic in <c>ElegantSeries.Flow.Core</c>; existing code
/// referencing this type keeps compiling unchanged.
/// </summary>
/// <remarks>
/// Abstracted (rather than using <c>Avalonia.Threading.Dispatcher</c> directly) so
/// host behavior can be unit tested without a running Avalonia application.
/// Production code should use <see cref="AvaloniaDispatcher"/>.
/// </remarks>
public interface IDispatcher : Core.Threading.IDispatcher
{
}
