using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Demonstrates asynchronous disposal (<see cref="IAsyncDisposable"/>): when
/// this page is navigated away from, the framework disposes its page scope
/// through the async path, which calls <see cref="DisposeAsync"/>.
/// </summary>
public sealed partial class AsyncDisposeDemoViewModel : BaseViewModel, IAsyncDisposable
{
    // Demo-only counter: proves DisposeAsync ran. A real app would not use a
    // static for this; it exists here so the recreated page can display how
    // many async disposals happened so far.
    private static int s_asyncDisposals;

    /// <summary>
    /// Proves transient recreation: it changes every time you revisit this page.
    /// </summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    public int AsyncDisposals => s_asyncDisposals;

    [RelayCommand]
    private Task OpenHomeAsync()
        => NavigateToAsync<HomeViewModel>("MainRegion");

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref s_asyncDisposals);
        return ValueTask.CompletedTask;
    }
}
