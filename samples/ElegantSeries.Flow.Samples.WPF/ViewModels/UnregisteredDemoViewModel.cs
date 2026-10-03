using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Deliberately has NO view registered: navigating to it exercises the
/// host's view-creation-failure path. It IS registered in DI so the
/// navigation itself succeeds and only the view creation fails.
/// </summary>
public sealed partial class UnregisteredDemoViewModel : BaseViewModel
{
}
