using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Bound-<c>RegionName</c> demo: a single <c>NavigationHost</c> whose
/// <c>RegionName</c> and <c>NavigationService</c> are data-bound (no code-behind
/// wiring for them; <c>ViewLocator</c> is still assigned in code-behind because
/// its interface is platform-specific and this ViewModel is shared).
/// Flipping <see cref="DisplayedRegion"/> immediately re-displays the other
/// region's current page — the host refreshes on rename instead of keeping the
/// previous region's stale view. Switching to an empty region clears the host.
/// </summary>
public sealed partial class RegionSwitchDemoViewModel : BaseViewModel, INavigationAware
{
    [ObservableProperty]
    private string _displayedRegion = RegionNames.SwitchA;

    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task PushCounterToAAsync()
        => NavigateToAsync<CounterViewModel>(RegionNames.SwitchA);

    [RelayCommand]
    private Task PushDetailToBAsync()
        => NavigateToAsync<DetailViewModel, string>("Hello from SwitchB", RegionNames.SwitchB);

    [RelayCommand]
    private void ShowA() => DisplayedRegion = RegionNames.SwitchA;

    [RelayCommand]
    private void ShowB() => DisplayedRegion = RegionNames.SwitchB;
}
