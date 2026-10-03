using Avalonia.Controls;
using AvaloniaSample.ViewModels;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaSample;

/// <summary>
/// Demonstrates <b>scoped navigation</b>: this window owns an
/// <see cref="INavigationService"/> built from its own DI scope, so its
/// region stacks are fully isolated from the main window's.
/// The scope is disposed together with the window.
/// </summary>
public partial class SecondWindow : Window
{
    private readonly IServiceScope _scope;
    private readonly INavigationService _navigation;

    public SecondWindow()
    {
        InitializeComponent();

        // Per-window scope -> per-window navigation service (not in DI).
        _scope = App.Services.CreateScope();
        _navigation = new NavigationService(_scope.ServiceProvider);
        var views = App.Services.GetRequiredService<IViewLocator>();

        WindowHost.NavigationService = _navigation;
        WindowHost.ViewLocator = views;

        SampleHelpers.ObserveNavigation(_navigation.NavigateToAsync<HomeViewModel>(), ReportError);
    }

    // Surfaces failures of the initial navigation instead of leaving an
    // unobserved fire-and-forget task.
    private void ReportError(string message) => ErrorText.Text = message;

    protected override void OnClosed(EventArgs e)
    {
        WindowHost.Dispose();
        (_navigation as IDisposable)?.Dispose();
        _scope.Dispose();
        base.OnClosed(e);
    }
}
