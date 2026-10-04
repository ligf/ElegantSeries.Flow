using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Q4 demo — <see cref="NavigationMode.Replace"/>: the current page is
/// swapped for the target type without growing the stack. Replace targets
/// a different type because replacing with the already-active type is a
/// no-op by design. The instance id proves a new page was created.
/// </summary>
public sealed partial class ReplaceDemoViewModel : BaseViewModel
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [RelayCommand]
    private Task ReplaceWithTargetAsync()
        => NavigateToAsync<ReplaceTargetViewModel>(RegionNames.Q4, NavigationMode.Replace);

    [RelayCommand]
    private Task PushTargetAsync()
        => NavigateToAsync<ReplaceTargetViewModel>(RegionNames.Q4);
}
