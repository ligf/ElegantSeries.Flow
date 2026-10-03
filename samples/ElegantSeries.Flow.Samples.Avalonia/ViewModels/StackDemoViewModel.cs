using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q1 demo — basic stack navigation inside one quadrant. Push alternates
/// between this type and <see cref="StackChildViewModel"/>: navigating to
/// the already-active type is a no-op by design, so a push must target a
/// different type.
/// </summary>
public sealed partial class StackDemoViewModel : BaseViewModel, INavigationAware<int>
{
    [ObservableProperty]
    private int _depth;

    /// <summary>
    /// Status line: explains why a navigation did nothing (e.g. going back
    /// at the root). Cleared on the next successful navigation.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Proves transient recreation: push, go back, push again — the id
    /// differs because the popped page scope was disposed.
    /// </summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    public void OnNavigatedTo(int parameter)
    {
        Depth = parameter;
        StatusMessage = string.Empty;
    }

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task PushDeeperAsync()
        => NavigateToAsync<StackChildViewModel, int>(Depth + 1, "Q1");

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (await base.GoBackAsync("Q1"))
            StatusMessage = string.Empty;
        else
            StatusMessage = "Already at the root — nothing to go back to.";
    }
}
