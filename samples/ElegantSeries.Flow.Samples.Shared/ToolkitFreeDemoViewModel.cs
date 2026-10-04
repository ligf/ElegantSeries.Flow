using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Toolkit-free ViewModel demo: derives from <see cref="NavigationViewModelBase"/>
/// (in the <c>ElegantSeries.Flow</c> core package) instead of the
/// CommunityToolkit.Mvvm-based <c>BaseViewModel</c>. Property notification uses
/// the base class's <c>SetProperty</c> helper; navigation uses the protected
/// <c>NavigateToAsync</c> / <c>GoBackAsync</c> helpers. No MVVM toolkit reference
/// needed — the view's buttons call the public methods from code-behind.
/// </summary>
public sealed class ToolkitFreeDemoViewModel : NavigationViewModelBase, INavigationAware
{
    private int _count;

    public int Count
    {
        get => _count;
        private set => SetProperty(ref _count, value);
    }

    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom() { }

    public void Increment() => Count++;

    public Task<bool> LeaveAsync() => GoBackAsync();
}
