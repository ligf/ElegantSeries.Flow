using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q5 demo — <see cref="NavigationMode.ClearStack"/>: push a few pages, then
/// clear the stack back to a single fresh page. The instance id proves the
/// cleared pages were disposed and a new one created.
/// </summary>
public sealed partial class ClearStackDemoViewModel : BaseViewModel
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [RelayCommand]
    private Task PushSelfAsync()
        => NavigateToAsync<ClearStackDemoViewModel>("Q5");

    [RelayCommand]
    private Task ClearStackAsync()
        => NavigateToAsync<ClearStackDemoViewModel>("Q5", NavigationMode.ClearStack);

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q5");
}
