using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q4 replace target: replace back, or go back when it was pushed.
/// </summary>
public sealed partial class ReplaceTargetViewModel : BaseViewModel
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [RelayCommand]
    private Task ReplaceWithDemoAsync()
        => NavigateToAsync<ReplaceDemoViewModel>("Q4", NavigationMode.Replace);

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q4");
}
