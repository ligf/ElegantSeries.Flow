using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Q3 demo — strongly-typed navigation parameters: each button pushes the
/// receiver page carrying a different parameter.
/// </summary>
public sealed partial class ParamDemoViewModel : BaseViewModel
{
    [RelayCommand]
    private Task SendHelloAsync()
        => NavigateToAsync<ParamReceiverViewModel, string>("Hello from Q3", "Q3");

    [RelayCommand]
    private Task SendWorldAsync()
        => NavigateToAsync<ParamReceiverViewModel, string>("World from Q3", "Q3");
}
