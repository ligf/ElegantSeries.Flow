using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Q4 child page used by the push / replace demos.
/// </summary>
public sealed partial class ModesChildViewModel : BaseViewModel
{
    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q4");
}
