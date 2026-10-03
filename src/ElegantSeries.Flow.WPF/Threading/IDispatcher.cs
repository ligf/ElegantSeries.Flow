namespace ElegantSeries.Flow.WPF.Threading;

/// <summary>
/// WPF UI-thread marshaling abstraction. Extends
/// <see cref="Core.Threading.IDispatcher"/> so dispatchers also satisfy the
/// shared host logic in <c>ElegantSeries.Flow.Core</c>; existing code
/// referencing this type keeps compiling unchanged.
/// </summary>
public interface IDispatcher : Core.Threading.IDispatcher
{
}
