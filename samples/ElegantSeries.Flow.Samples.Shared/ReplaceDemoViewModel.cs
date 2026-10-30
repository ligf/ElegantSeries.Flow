using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Q4 demo — <see cref="NavigationOperation.Replace"/>: the current page is
/// swapped for the target type without growing the stack. The instance id
/// proves a new page was created. Replacing with the already-active type
/// is not a no-op either: the no-op shortcut applies only to a default
/// Push + Destroy request, so an explicit Replace still leaves the
/// current page and creates a fresh instance.
/// </summary>
public sealed partial class ReplaceDemoViewModel : BaseViewModel
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [RelayCommand]
    private Task ReplaceWithTargetAsync()
        => NavigateToAsync<ReplaceTargetViewModel>(RegionNames.Q4, operation: NavigationOperation.Replace);

    [RelayCommand]
    private Task PushTargetAsync()
        => NavigateToAsync<ReplaceTargetViewModel>(RegionNames.Q4);
}
