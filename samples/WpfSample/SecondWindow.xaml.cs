using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using WpfSample.ViewModels;

namespace WpfSample;

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

        _ = _navigation.NavigateToAsync<HomeViewModel>();
    }

    protected override void OnClosed(EventArgs e)
    {
        WindowHost.Dispose();
        (_navigation as IDisposable)?.Dispose();
        _scope.Dispose();
        base.OnClosed(e);
    }
}
