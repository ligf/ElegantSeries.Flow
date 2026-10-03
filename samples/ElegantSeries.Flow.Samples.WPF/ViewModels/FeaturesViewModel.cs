using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Demonstrates navigation modes (<see cref="NavigationMode.Replace"/>,
/// <see cref="NavigationMode.ClearStack"/>), <c>refreshIfActive</c>,
/// <see cref="INavigationService.ClearCache"/>, cooperative cancellation,
/// and the async lifecycle (<see cref="INavigationAwareAsync"/>).
/// </summary>
public sealed partial class FeaturesViewModel : BaseViewModel, INavigationAwareAsync
{
    [ObservableProperty]
    private string _log = string.Empty;

    [ObservableProperty]
    private int _refreshCount;

    [ObservableProperty]
    private string _cancelStatus = "Idle";

    private void AppendLog(string line) => Log += line + "\n";

    public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        AppendLog($"OnNavigatedToAsync (param: {parameter ?? "null"})");
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
    {
        AppendLog("OnNavigatedFromAsync");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ReplaceWithDetailAsync()
        => NavigateToAsync<DetailViewModel, string>(
            "Replaced via NavigationMode.Replace", "MainRegion", NavigationMode.Replace);

    [RelayCommand]
    private Task ClearStackToHomeAsync()
        => NavigateToAsync<HomeViewModel>("MainRegion", NavigationMode.ClearStack);

    [RelayCommand]
    private Task RefreshSelfAsync()
    {
        RefreshCount++;
        // Already active -> no push, callbacks re-run with the new parameter.
        return NavigateToAsync<FeaturesViewModel, string>(
            $"refresh #{RefreshCount}", "MainRegion", refreshIfActive: true);
    }

    [RelayCommand]
    private void ClearKeepAliveCache()
    {
        Navigation?.ClearCache("MainRegion");
        AppendLog("ClearCache(\"MainRegion\") — cached KeepAlive pages disposed.");
    }

    [RelayCommand]
    private async Task CancelledNavigationAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already-cancelled: the API throws before touching the stack
        try
        {
            await NavigateToAsync<HomeViewModel>("MainRegion", cancellationToken: cts.Token);
            CancelStatus = "Unexpected: navigation succeeded.";
        }
        catch (OperationCanceledException)
        {
            CancelStatus = "Cancelled as expected (OperationCanceledException).";
        }
        AppendLog($"Cancellation demo: {CancelStatus}");
    }
}
