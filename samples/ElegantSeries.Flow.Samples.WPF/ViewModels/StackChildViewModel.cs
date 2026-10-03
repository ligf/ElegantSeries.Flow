using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Q1 child page: push alternates back to <see cref="StackDemoViewModel"/>.
/// </summary>
public sealed partial class StackChildViewModel : BaseViewModel, INavigationAware<int>
{
    [ObservableProperty]
    private int _depth;

    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    public void OnNavigatedTo(int parameter) => Depth = parameter;

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private Task PushDeeperAsync()
        => NavigateToAsync<StackDemoViewModel, int>(Depth + 1, "Q1");

    [RelayCommand]
    private Task GoBackAsync() => base.GoBackAsync("Q1");
}
