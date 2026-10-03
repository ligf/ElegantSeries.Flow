using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Demonstrates <see cref="INavigationGuard"/> and
/// <see cref="INavigationGuardWithContext"/>: when <see cref="IsDirty"/> is set,
/// navigating away is vetoed.
/// <para>
/// Framework priority (<c>NavigationService.InvokeFromGuardAsync</c>): when a
/// ViewModel implements <see cref="INavigationGuardWithContext"/>, it is
/// consulted <i>instead of</i> <see cref="INavigationGuard"/>. The
/// <see cref="UsePlainGuard"/> flag therefore does not change <i>which</i>
/// interface the framework calls — it changes which logic the context guard
/// executes, so you can compare the two: the plain guard cannot name the
/// target page/region, the context guard can.
/// </para>
/// </summary>
public sealed partial class GuardedViewModel : BaseViewModel, INavigationGuard, INavigationGuardWithContext
{
    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _usePlainGuard;

    [ObservableProperty]
    private string _blockMessage = string.Empty;

    // Plain guard: no context — the veto rule is the same, but the message
    // cannot say where the navigation was headed.
    public Task<bool> CanNavigateFromAsync()
    {
        if (!IsDirty)
            return Task.FromResult(true);

        BlockMessage = "Blocked (plain guard): unsaved changes. Save or discard first.";
        return Task.FromResult(false);
    }

    public Task<bool> CanNavigateFromAsync(NavigationGuardContext context)
    {
        // This method always wins when implemented (see the class doc); the
        // flag only selects which logic runs inside it.
        if (UsePlainGuard)
            return CanNavigateFromAsync();

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
