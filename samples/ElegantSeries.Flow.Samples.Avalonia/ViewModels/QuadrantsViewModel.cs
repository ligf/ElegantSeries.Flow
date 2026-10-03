using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Container page for the quadrant demo: hosts six independent regions
/// (Q1–Q6), each running its own single-feature demo.
/// </summary>
public sealed partial class QuadrantsViewModel : BaseViewModel
{
    public string Title => "Quadrants (Q1-Q6 independent regions)";

    /// <summary>
    /// Status line for the external controls below.
    /// </summary>
    [ObservableProperty]
    private string _externalStatus = string.Empty;

    /// <summary>
    /// Drives Q1 from outside the quadrant: regions are addressable by name,
    /// so any code holding the navigation service can navigate them — the
    /// buttons do not have to live inside the quadrant. Push alternates page
    /// types because navigating to the already-active type is a no-op.
    /// </summary>
    [RelayCommand]
    private Task ExternalPushQ1Async()
    {
        ExternalStatus = string.Empty;
        var current = Navigation?.GetCurrentViewModel("Q1");
        var depth = current switch
        {
            StackDemoViewModel root => root.Depth + 1,
            StackChildViewModel child => child.Depth + 1,
            _ => 0,
        };

        return current is StackDemoViewModel
            ? NavigateToAsync<StackChildViewModel, int>(depth, "Q1")
            : NavigateToAsync<StackDemoViewModel, int>(depth, "Q1");
    }

    [RelayCommand]
    private async Task ExternalBackQ1Async()
    {
        if (await base.GoBackAsync("Q1"))
            ExternalStatus = string.Empty;
        else
            ExternalStatus = "Q1 is already at its root — nothing to go back to.";
    }
}
