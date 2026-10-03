using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q3 receiver page: displays the strongly-typed parameter it was
/// navigated with.
/// </summary>
public sealed partial class ParamReceiverViewModel : BaseViewModel, INavigationAware<string>
{
    [ObservableProperty]
    private string _message = string.Empty;

    public void OnNavigatedTo(string parameter) => Message = parameter;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q3");
}
