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
        services.AddTransient<AsyncDemoViewModel>();
        services.AddTransient<CacheDemoViewModel>();
        services.AddTransient<CancelDemoViewModel>();
        services.AddTransient<QuadrantsViewModel>();
        services.AddTransient<StackDemoViewModel>();
        services.AddTransient<StackChildViewModel>();
        services.AddTransient<KeepAliveDemoViewModel>();
        services.AddTransient<KeepAliveTempViewModel>();
        services.AddTransient<ParamDemoViewModel>();
        services.AddTransient<ParamReceiverViewModel>();
        services.AddTransient<ReplaceDemoViewModel>();
        services.AddTransient<ReplaceTargetViewModel>();
        services.AddTransient<ClearStackDemoViewModel>();
        services.AddTransient<ClearStackChildViewModel>();
        services.AddTransient<RefreshDemoViewModel>();

        // AOT-safe view registration: no runtime reflection.
        services.AddFlowViews(views =>
        {
            views.Register<MenuView, MenuViewModel>();
            views.Register<HomeView, HomeViewModel>();
            views.Register<DetailView, DetailViewModel>();
            views.Register<CounterView, CounterViewModel>();
            views.Register<GuardedView, GuardedViewModel>();
            views.Register<AsyncDemoView, AsyncDemoViewModel>();
            views.Register<CacheDemoView, CacheDemoViewModel>();
            views.Register<CancelDemoView, CancelDemoViewModel>();
            views.Register<QuadrantsView, QuadrantsViewModel>();
            views.Register<StackDemoView, StackDemoViewModel>();
            views.Register<StackChildView, StackChildViewModel>();
            views.Register<KeepAliveDemoView, KeepAliveDemoViewModel>();
            views.Register<KeepAliveTempView, KeepAliveTempViewModel>();
            views.Register<ParamDemoView, ParamDemoViewModel>();
            views.Register<ParamReceiverView, ParamReceiverViewModel>();
            views.Register<ReplaceDemoView, ReplaceDemoViewModel>();
            views.Register<ReplaceTargetView, ReplaceTargetViewModel>();
            views.Register<ClearStackDemoView, ClearStackDemoViewModel>();
            views.Register<ClearStackChildView, ClearStackChildViewModel>();
            views.Register<RefreshDemoView, RefreshDemoViewModel>();
        });

        Services = services.BuildServiceProvider();

        base.OnStartup(e);
    }
}
