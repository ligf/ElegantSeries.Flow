using System.Windows;
using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.WPF.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.WPF.ViewModels;
using ElegantSeries.Flow.Samples.WPF.Views;

namespace ElegantSeries.Flow.Samples.WPF;

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
        services.AddTransient<GuardedViewModel>();
        services.AddTransient<FeaturesViewModel>();
        services.AddTransient<QuadrantsViewModel>();
        services.AddTransient<StackDemoViewModel>();
        services.AddTransient<KeepAliveDemoViewModel>();
        services.AddTransient<KeepAliveTempViewModel>();
        services.AddTransient<ParamDemoViewModel>();
        services.AddTransient<ModesDemoViewModel>();
        services.AddTransient<ModesChildViewModel>();

        // AOT-safe view registration: no runtime reflection.
        services.AddFlowViews(views =>
        {
            views.Register<MenuView, MenuViewModel>();
            views.Register<HomeView, HomeViewModel>();
            views.Register<DetailView, DetailViewModel>();
            views.Register<CounterView, CounterViewModel>();
            views.Register<GuardedView, GuardedViewModel>();
            views.Register<FeaturesView, FeaturesViewModel>();
            views.Register<QuadrantsView, QuadrantsViewModel>();
            views.Register<StackDemoView, StackDemoViewModel>();
            views.Register<KeepAliveDemoView, KeepAliveDemoViewModel>();
            views.Register<KeepAliveTempView, KeepAliveTempViewModel>();
            views.Register<ParamDemoView, ParamDemoViewModel>();
            views.Register<ModesDemoView, ModesDemoViewModel>();
            views.Register<ModesChildView, ModesChildViewModel>();
        });

        Services = services.BuildServiceProvider();

        base.OnStartup(e);
    }
}
