using CommunityToolkit.Mvvm.ComponentModel;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Demonstrates the asynchronous lifecycle (<see cref="INavigationAwareAsync{TParam}"/>):
/// a visible log of <c>OnNavigatedToAsync</c> / <c>OnNavigatedFromAsync</c> with
/// a strongly-typed navigation parameter.
/// </summary>
public sealed partial class AsyncDemoViewModel : BaseViewModel, INavigationAwareAsync<string>
{
    [ObservableProperty]
    private string _log = string.Empty;

    private void AppendLog(string line) => Log += line + "\n";

    public Task OnNavigatedToAsync(string parameter, CancellationToken cancellationToken = default)
    {
        AppendLog($"OnNavigatedToAsync (param: {parameter ?? "null"})");
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
    {
        AppendLog("OnNavigatedFromAsync");
        return Task.CompletedTask;
    }
}
