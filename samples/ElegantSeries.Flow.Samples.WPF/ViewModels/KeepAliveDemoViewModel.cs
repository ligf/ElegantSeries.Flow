using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Q2 demo — <see cref="ElegantSeries.Flow.Core.Navigation.NavigationMode.KeepAlive"/>:
/// open the temp page and come back, the count is preserved because the page
/// (ViewModel + its scope) is cached instead of disposed.
/// </summary>
public sealed partial class KeepAliveDemoViewModel : BaseViewModel
{
    [ObservableProperty]
    private int _count;

    [RelayCommand]
    private void Increment() => Count++;

    [RelayCommand]
    private Task OpenTempPageAsync()
        => NavigateToAsync<KeepAliveTempViewModel>("Q2");
}
