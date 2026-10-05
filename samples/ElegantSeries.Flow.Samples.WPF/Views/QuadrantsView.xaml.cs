using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.WPF.Hosting;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor]
public partial class QuadrantsView : ElegantSeries.Flow.WPF.Views.BaseView<QuadrantsViewModel>
{
    private readonly NavigationHost[] _hosts;

    public QuadrantsView()
    {
        InitializeComponent();
        _hosts = [Q1Host, Q2Host, Q3Host, Q4Host, Q5Host, Q6Host];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Use the NavigationService that owns this page (works in the main
        // window and in isolated second windows); fall back to the root.
        var navigation = ViewModel?.Navigation
            ?? App.Services.GetRequiredService<INavigationService>();
        var views = App.Services.GetRequiredService<IViewLocator>();

        foreach (var host in _hosts)
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }

        // Hosts do not replay missed events, so quadrants are navigated after
        // the hosts attach. refreshIfActive re-activates the already-active
        // page (raising RegionNavigated for the fresh hosts) instead of
        // pushing a duplicate when this page is revisited.
        // Each quadrant navigates independently: one failure must not block
        // the others.
        await TryNavigate(() => navigation.NavigateToAsync<StackDemoViewModel, int>(0, RegionNames.Q1, refreshIfActive: true));
        await TryNavigate(() => navigation.NavigateToAsync<KeepAliveDemoViewModel>(RegionNames.Q2, NavigationMode.KeepAlive, refreshIfActive: true));
        await TryNavigate(() => navigation.NavigateToAsync<ParamDemoViewModel, string>("Initial parameter", RegionNames.Q3, refreshIfActive: true));
        await TryNavigate(() => navigation.NavigateToAsync<ReplaceDemoViewModel>(RegionNames.Q4, refreshIfActive: true));
        await TryNavigate(() => navigation.NavigateToAsync<ClearStackDemoViewModel>(RegionNames.Q5, refreshIfActive: true));
        await TryNavigate(() => navigation.NavigateToAsync<RefreshDemoViewModel>(RegionNames.Q6, refreshIfActive: true));

        static async Task TryNavigate(Func<Task<bool>> navigate)
        {
            try
            {
                await navigate();
            }
            catch
            {
                // Quadrant failures are isolated; the other quadrants still load.
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        foreach (var host in _hosts)
            host.Dispose();
    }
}
