using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.ViewModels;

/// <summary>
/// Demonstrates <see cref="INavigationService.ClearCache"/> and
/// <see cref="INavigationService.ClearAllCache"/>: cached KeepAlive
/// pages are disposed on demand. Script: open the Counter (KeepAlive),
/// increment, come back here, ClearCache, open the Counter again — the count
/// is reset because the cached page was disposed. Clear All Caches does the
/// same for every region at once.
/// </summary>
public sealed partial class CacheDemoViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _log = string.Empty;

    private void AppendLog(string line) => Log += line + "\n";

    [RelayCommand]
    private Task OpenCounterAsync()
        => NavigateToAsync<CounterViewModel>("MainRegion", NavigationMode.KeepAlive);

    [RelayCommand]
    private void ClearKeepAliveCache()
    {
        Navigation?.ClearCache("MainRegion");
        AppendLog("ClearCache(\"MainRegion\") — cached KeepAlive pages disposed.");
    }

    [RelayCommand]
    private void ClearAllCaches()
    {
        Navigation?.ClearAllCache();
        AppendLog("ClearAllCache() — every region's KeepAlive cache cleared.");
    }
}
