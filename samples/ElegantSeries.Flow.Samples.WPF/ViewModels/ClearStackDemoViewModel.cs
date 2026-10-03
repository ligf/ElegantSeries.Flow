using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Q5 demo — <see cref="NavigationMode.ClearStack"/>: push a few pages, then
/// clear the stack back to a single fresh page. Push alternates with
/// <see cref="ClearStackChildViewModel"/> because pushing the already-active
/// type is a no-op by design.
/// </summary>
public sealed partial class ClearStackDemoViewModel : BaseViewModel, INavigationAware
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Status line: explains why a navigation did nothing (e.g. ClearStack
    /// while already the active page). Cleared whenever the page becomes
    /// active again.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public void OnNavigatedTo(object? parameter) => StatusMessage = string.Empty;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task PushChildAsync()
        => NavigateToAsync<ClearStackChildViewModel>("Q5");

    [RelayCommand]
    private Task ClearStackAsync()
    {
        if (Navigation?.GetCurrentViewModel("Q5") is ClearStackDemoViewModel)
        {
            StatusMessage = "Already the active page — ClearStack ignored (no-op).";
            return Task.CompletedTask;
        }

        return NavigateToAsync<ClearStackDemoViewModel>("Q5", NavigationMode.ClearStack);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (await base.GoBackAsync("Q5"))
            StatusMessage = string.Empty;
        else
            StatusMessage = "Already at the root — nothing to go back to.";
    }
}
