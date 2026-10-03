using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Q5 child page: push back to the demo type, or clear the whole stack.
/// </summary>
public sealed partial class ClearStackChildViewModel : BaseViewModel
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [RelayCommand]
    private Task PushDemoAsync()
        => NavigateToAsync<ClearStackDemoViewModel>("Q5");

    [RelayCommand]
    private Task ClearStackAsync()
        => NavigateToAsync<ClearStackDemoViewModel>("Q5", NavigationMode.ClearStack);

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q5");
}
