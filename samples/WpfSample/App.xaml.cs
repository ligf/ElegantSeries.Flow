using System.Windows;
using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.WPF.Extensions;
using Microsoft.Extensions.DependencyInjection;
using WpfSample.ViewModels;
using WpfSample.Views;

namespace WpfSample;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        // Core navigation.
        services.AddFlowNavigation();

        // ViewModels. MenuViewModel is a singleton to demonstrate that singleton
        // ViewModels coexist fine with transient pages: the page scope resolves
        // the shared root instance and never disposes it.
        services.AddSingleton<MenuViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<DetailViewModel>();
        services.AddTransient<CounterViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<GuardedViewModel>();
        services.AddTransient<FeaturesViewModel>();

        // AOT-safe view registration: no runtime reflection.
        services.AddFlowViews(views =>
        {
            views.Register<MenuView, MenuViewModel>();
            views.Register<HomeView, HomeViewModel>();
            views.Register<DetailView, DetailViewModel>();
            views.Register<CounterView, CounterViewModel>();
            views.Register<DashboardView, DashboardViewModel>();
            views.Register<GuardedView, GuardedViewModel>();
            views.Register<FeaturesView, FeaturesViewModel>();
        });

        Services = services.BuildServiceProvider();

        base.OnStartup(e);
    }
}
