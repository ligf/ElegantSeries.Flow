using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Q4 replace target: replace back, or go back when it was pushed.
/// </summary>
public sealed partial class ReplaceTargetViewModel : BaseViewModel
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Status line: explains why a navigation did nothing. Cleared on the
    /// next successful navigation.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private Task ReplaceWithDemoAsync()
        => NavigateToAsync<ReplaceDemoViewModel>(RegionNames.Q4, NavigationMode.Replace);

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (await base.GoBackAsync(RegionNames.Q4))
            StatusMessage = string.Empty;
        else
            StatusMessage = "Nowhere to go back to — Replace does not grow the stack.";
    }
}
