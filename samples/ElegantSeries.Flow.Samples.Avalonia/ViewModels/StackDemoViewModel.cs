using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Q1 demo — basic stack navigation inside one quadrant: push deeper pages,
/// go back. Each push creates a new page scope; going back disposes it.
/// </summary>
public sealed partial class StackDemoViewModel : BaseViewModel, INavigationAware<int>
{
    [ObservableProperty]
    private int _depth;

    public void OnNavigatedTo(int parameter) => Depth = parameter;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task PushDeeperAsync()
        => NavigateToAsync<StackDemoViewModel, int>(Depth + 1, "Q1");

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q1");
}
