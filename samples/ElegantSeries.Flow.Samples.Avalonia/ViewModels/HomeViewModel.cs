using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Avalonia.ViewModels;

public sealed partial class HomeViewModel : BaseViewModel
{
    public string Title => "Home";

    /// <summary>
    /// Proves transient recreation: navigate away and back, the id changes
    /// because the old page scope was disposed and a new one created.
    /// </summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    [RelayCommand]
    private Task GoToDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("Hello from Home");
}
