using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Demonstrates cooperative cancellation: a navigation with an
/// already-cancelled token throws <see cref="OperationCanceledException"/>
/// before touching the region stack.
/// </summary>
public sealed partial class CancelDemoViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _status = "Idle";

    [RelayCommand]
    private async Task CancelledNavigationAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already-cancelled: the API throws before touching the stack
        try
        {
            await NavigateToAsync<HomeViewModel>("MainRegion", cancellationToken: cts.Token);
            Status = "Unexpected: navigation succeeded.";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled as expected (OperationCanceledException).";
        }
    }
}
