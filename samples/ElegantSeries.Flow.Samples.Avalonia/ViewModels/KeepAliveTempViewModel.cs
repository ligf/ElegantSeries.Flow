using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q2 temp page: leaving the KeepAlive page and coming back via "返回".
/// </summary>
public sealed partial class KeepAliveTempViewModel : BaseViewModel
{
    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q2");
}
