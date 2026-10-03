using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q4 demo — navigation modes: push, replace, clear-stack and refresh.
/// The instance id proves whether the page was reused (refresh) or recreated
/// (replace / clear-stack).
/// </summary>
public sealed partial class ModesDemoViewModel : BaseViewModel, INavigationAware
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [ObservableProperty]
    private int _activationCount;

    public void OnNavigatedTo(object? parameter) => ActivationCount++;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task PushChildAsync()
        => NavigateToAsync<ModesChildViewModel>("Q4");

    [RelayCommand]
    private Task ReplaceWithChildAsync()
        => NavigateToAsync<ModesChildViewModel>("Q4", NavigationMode.Replace);

    [RelayCommand]
    private Task ClearStackAsync()
        => NavigateToAsync<ModesDemoViewModel>("Q4", NavigationMode.ClearStack);

    [RelayCommand]
    private Task RefreshAsync()
        => NavigateToAsync<ModesDemoViewModel>("Q4", refreshIfActive: true);
}
