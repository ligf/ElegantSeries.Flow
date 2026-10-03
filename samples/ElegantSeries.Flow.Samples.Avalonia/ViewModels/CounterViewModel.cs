using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

/// <summary>
/// Demonstrates <see cref="ElegantSeries.Flow.Core.Navigation.NavigationMode.KeepAlive"/>:
/// navigate away and back — the count is preserved because the page (ViewModel +
/// its scope) is cached instead of disposed.
/// </summary>
public sealed partial class CounterViewModel : BaseViewModel
{
    [ObservableProperty]
    private int _count;

    [RelayCommand]
    private void Increment() => Count++;
}
