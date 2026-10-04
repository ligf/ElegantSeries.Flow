using System.Diagnostics.CodeAnalysis;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;
using ElegantSeries.Flow.Core.Threading;

namespace ElegantSeries.Flow.WPF.Tests;

// Test doubles shared by both test legs (net10.0 logic tests and net10.0-windows UI tests).

internal class StubViewModel : NavigationViewModelBase
{
}

internal class OtherViewModel : NavigationViewModelBase
{
}

/// <summary>
/// Controllable <see cref="IDispatcher"/> test double: records posted actions
/// instead of marshaling them; the test pumps them explicitly via <see cref="RunPosted"/>.
/// </summary>
internal sealed class FakeDispatcher : IDispatcher
{
    private readonly bool _hasAccess;
    private readonly List<Action> _posted = new();
    private readonly Lock _syncRoot = new();

    public FakeDispatcher(bool hasAccess = true)
    {
        _hasAccess = hasAccess;
    }

    public int PostedCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _posted.Count;
            }
        }
    }

    public bool CheckAccess() => _hasAccess;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_syncRoot)
        {
            _posted.Add(action);
        }
    }

    /// <summary>
    /// Runs all posted actions on the calling thread, in posting order.
    /// </summary>
    public void RunPosted()
    {
        List<Action> batch;
        lock (_syncRoot)
        {
            batch = new List<Action>(_posted);
            _posted.Clear();
        }

        foreach (var action in batch)
        {
            action();
        }
    }
}

/// <summary>
/// Minimal <see cref="INavigationService"/> test double: only
/// <see cref="INavigationService.RegionNavigated"/> is functional; all other
/// members throw <see cref="NotImplementedException"/>.
/// </summary>
internal sealed class FakeNavigationService : INavigationService
{
    public event Action<string, INavigationViewModel>? RegionNavigated;
    public event Action<string, INavigationViewModel>? ViewModelReleased;
    public event Action<string>? RegionCacheCleared;

    /// <summary>
    /// Raises <see cref="RegionNavigated"/> synchronously on the calling thread.
    /// </summary>
    public void RaiseNavigated(string regionName, INavigationViewModel viewModel)
        => RegionNavigated?.Invoke(regionName, viewModel);

    /// <summary>
    /// Raises <see cref="ViewModelReleased"/> synchronously on the calling thread.
    /// </summary>
    public void RaiseReleased(string regionName, INavigationViewModel viewModel)
        => ViewModelReleased?.Invoke(regionName, viewModel);

    /// <summary>
    /// Raises <see cref="RegionCacheCleared"/> synchronously on the calling thread.
    /// </summary>
    public void RaiseCacheCleared(string regionName)
        => RegionCacheCleared?.Invoke(regionName);

    public bool CanGoBack(string regionName = "MainRegion") => throw new NotImplementedException();
    public Dictionary<string, INavigationViewModel> CurrentByRegion { get; } = new();
    public INavigationViewModel? GetCurrentViewModel(string regionName = "MainRegion")
        => CurrentByRegion.TryGetValue(regionName, out var vm) ? vm : null;
    public bool IsActive<TViewModel>(string regionName = "MainRegion") where TViewModel : INavigationViewModel => throw new NotImplementedException();
    public NavigationMode? GetCurrentMode(string regionName = "MainRegion") => throw new NotImplementedException();
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(string regionName = "MainRegion", NavigationMode mode = NavigationMode.New, bool refreshIfActive = false, CancellationToken cancellationToken = default) where TViewModel : INavigationViewModel => throw new NotImplementedException();
    public Task<bool> NavigateToAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(TParam parameter, string regionName = "MainRegion", NavigationMode mode = NavigationMode.New, bool refreshIfActive = false, CancellationToken cancellationToken = default) where TViewModel : INavigationViewModel => throw new NotImplementedException();
    public Task<bool> GoBackAsync(string regionName = "MainRegion", CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public void ClearCache(string regionName) => throw new NotImplementedException();
    public void ClearAllCache() => throw new NotImplementedException();
    public ValueTask ClearCacheAsync(string regionName) => throw new NotImplementedException();
    public ValueTask ClearAllCacheAsync() => throw new NotImplementedException();
}
