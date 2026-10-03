using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q3 demo — strongly-typed navigation parameters: each button pushes a new
/// page carrying a different parameter; going back reveals the previous one.
/// </summary>
public sealed partial class ParamDemoViewModel : BaseViewModel, INavigationAware<string>
{
    [ObservableProperty]
    private string _message = string.Empty;

    public void OnNavigatedTo(string parameter) => Message = parameter;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task SendHelloAsync()
        => NavigateToAsync<ParamDemoViewModel, string>("Hello from Q3", "Q3");

    [RelayCommand]
    private Task SendWorldAsync()
        => NavigateToAsync<ParamDemoViewModel, string>("World from Q3", "Q3");

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q3");
}
