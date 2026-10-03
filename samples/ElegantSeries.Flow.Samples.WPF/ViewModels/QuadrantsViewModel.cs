using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Container page for the quadrant demo: hosts six independent regions
/// (Q1–Q6), each running its own single-feature demo.
/// </summary>
public sealed partial class QuadrantsViewModel : BaseViewModel
{
    public string Title => "Quadrants (Q1-Q6 independent regions)";
}
