using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Q6 demo — <c>refreshIfActive</c>: re-running the active page's activation
/// callbacks with a new parameter instead of pushing. The instance id stays
/// the same while the activation count grows.
/// </summary>
public sealed partial class RefreshDemoViewModel : BaseViewModel, INavigationAware
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [ObservableProperty]
    private int _activationCount;

    public void OnNavigatedTo(object? parameter) => ActivationCount++;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task RefreshAsync()
        => NavigateToAsync<RefreshDemoViewModel>("Q6", refreshIfActive: true);
}
