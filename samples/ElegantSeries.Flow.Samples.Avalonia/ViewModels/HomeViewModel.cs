using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

public sealed partial class HomeViewModel : BaseViewModel
{
    public string Title => "Home";

    [RelayCommand]
    private Task GoToDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("Hello from Home");
}
