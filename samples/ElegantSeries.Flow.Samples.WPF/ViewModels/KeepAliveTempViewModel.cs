using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Q2 temp page: leaving the KeepAlive page and coming back via "返回".
/// </summary>
public sealed partial class KeepAliveTempViewModel : BaseViewModel
{
    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q2");
}
