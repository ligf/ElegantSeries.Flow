using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace WpfSample.ViewModels;

/// <summary>
/// Demonstrates <see cref="INavigationGuardWithContext"/>: when <see cref="IsDirty"/>
/// is set, navigating away is vetoed. The context tells the guard where the
/// navigation is headed.
/// </summary>
public sealed partial class GuardedViewModel : BaseViewModel, INavigationGuardWithContext
{
    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _blockMessage = string.Empty;

    public Task<bool> CanNavigateFromAsync(NavigationGuardContext context)
    {
        if (!IsDirty)
            return Task.FromResult(true);

        BlockMessage = $"Blocked: unsaved changes (tried to go to {context.TargetViewModelType.Name} in '{context.RegionName}'). Save or discard first.";
        return Task.FromResult(false);
    }

    [RelayCommand]
    private void Save()
    {
        IsDirty = false;
        BlockMessage = "Saved — navigation is allowed again.";
    }

    [RelayCommand]
    private void Discard()
    {
        IsDirty = false;
        BlockMessage = "Discarded — navigation is allowed again.";
    }
}
