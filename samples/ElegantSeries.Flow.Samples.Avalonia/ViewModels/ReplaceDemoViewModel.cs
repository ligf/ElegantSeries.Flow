using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q4 demo — <see cref="NavigationMode.Replace"/>: the current page is
/// swapped for a new one without growing the stack. The instance id proves
/// a new page was created; going back is a no-op when nothing is below.
/// </summary>
public sealed partial class ReplaceDemoViewModel : BaseViewModel
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [RelayCommand]
    private Task ReplaceSelfAsync()
        => NavigateToAsync<ReplaceDemoViewModel>("Q4", NavigationMode.Replace);

    [RelayCommand]
    private Task PushSelfAsync()
        => NavigateToAsync<ReplaceDemoViewModel>("Q4");

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q4");
}
