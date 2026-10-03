using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

public sealed partial class DetailViewModel : BaseViewModel, INavigationAware<string>
{
    [ObservableProperty]
    private string _message = string.Empty;

    public void OnNavigatedTo(string parameter) => Message = parameter;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync();
}
