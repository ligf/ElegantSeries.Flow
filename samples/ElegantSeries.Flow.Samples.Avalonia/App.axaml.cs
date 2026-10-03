using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ElegantSeries.Flow.Avalonia.Extensions;
using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.Avalonia.ViewModels;
using ElegantSeries.Flow.Samples.Avalonia.Views;

namespace ElegantSeries.Flow.Samples.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Core navigation.
        services.AddFlowNavigation();

        // The combined RegisterTransient/RegisterSingleton calls below cover
        // the common cases (view mapping + DI registration in one). The
        // separate style (services.AddTransient + views.Register) is kept for
        // Home and Detail to demonstrate the decoupled alternative — use it
        // when a ViewModel needs custom DI setup (factory, decorators, keyed
        // services, ...).
        services.AddTransient<HomeViewModel>();
        services.AddTransient<DetailViewModel>();

        // AOT-safe view registration: no runtime reflection.
        services.AddFlowViews(views =>
        {
            // Singleton ViewModel: coexists fine with transient pages — the
            // page scope resolves the shared root instance and never disposes
            // it.
            views.RegisterSingleton<MenuView, MenuViewModel>();
            views.Register<HomeView, HomeViewModel>();
            views.Register<DetailView, DetailViewModel>();
            views.RegisterTransient<CounterView, CounterViewModel>();
            views.RegisterTransient<GuardedView, GuardedViewModel>();
            views.RegisterTransient<AsyncDemoView, AsyncDemoViewModel>();
            views.RegisterTransient<CacheDemoView, CacheDemoViewModel>();
            views.RegisterTransient<CancelDemoView, CancelDemoViewModel>();
            views.RegisterTransient<QuadrantsView, QuadrantsViewModel>();
            views.RegisterTransient<StackDemoView, StackDemoViewModel>();
            views.RegisterTransient<StackChildView, StackChildViewModel>();
            views.RegisterTransient<KeepAliveDemoView, KeepAliveDemoViewModel>();
            views.RegisterTransient<KeepAliveTempView, KeepAliveTempViewModel>();
            views.RegisterTransient<ParamDemoView, ParamDemoViewModel>();
            views.RegisterTransient<ParamReceiverView, ParamReceiverViewModel>();
            views.RegisterTransient<ReplaceDemoView, ReplaceDemoViewModel>();
            views.RegisterTransient<ReplaceTargetView, ReplaceTargetViewModel>();
            views.RegisterTransient<ClearStackDemoView, ClearStackDemoViewModel>();
            views.RegisterTransient<ClearStackChildView, ClearStackChildViewModel>();
            views.RegisterTransient<RefreshDemoView, RefreshDemoViewModel>();
        });

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            // Show the first page.
            var navigation = Services.GetRequiredService<INavigationService>();
            _ = navigation.NavigateToAsync<HomeViewModel>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
