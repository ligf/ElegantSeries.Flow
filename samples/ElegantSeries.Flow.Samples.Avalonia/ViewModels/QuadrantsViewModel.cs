using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Container page for the quadrant demo: hosts four independent regions
/// (Q1–Q4), each running its own demo.
/// </summary>
public sealed partial class QuadrantsViewModel : BaseViewModel
{
    public string Title => "四宫格 (Q1-Q4 独立导航)";
}
