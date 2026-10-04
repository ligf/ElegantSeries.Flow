using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ElegantSeries.Flow.Avalonia.Extensions;
using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.Shared;
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
        services.AddSingletonFlowNavigation();

        // The combined RegisterTransient/RegisterSingleton calls below cover
        // the common cases (view mapping + DI registration in one). The
        // separate style (services.AddTransient + views.Register) is kept for
        // Home and Detail to demonstrate the decoupled alternative — use it
        // when a ViewModel needs custom DI setup (factory, decorators, keyed
        // services, ...).
        services.AddTransient<HomeViewModel>();
        services.AddTransient<DetailViewModel>();
        services.AddTransient<AsyncDisposeDemoViewModel>();
        services.AddTransient<FactoryDemoViewModel>();
        // DI only: deliberately no view registered — navigating to it
        // exercises the host's view-creation-failure path.
        services.AddTransient<UnregisteredDemoViewModel>();

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
            views.RegisterTransient<RegionSwitchView, RegionSwitchDemoViewModel>();
            views.RegisterTransient<StateInspectorView, StateInspectorViewModel>();
            views.RegisterTransient<EventsView, EventsDemoViewModel>();
            views.RegisterTransient<ToolkitFreeView, ToolkitFreeDemoViewModel>();
            views.RegisterTransient<AsyncDisposeView, AsyncDisposeDemoViewModel>();
            // Custom view factory: the factory overload does not touch DI, so
            // the ViewModel needs its own DI registration (see above). Use a
            // factory when the view needs constructor arguments or other
            // custom construction logic.
            views.Register<FactoryDemoViewModel>(_ =>
            {
                var view = new FactoryDemoView();
                view.ApplyFactoryBadge();
                return view;
            });
            views.RegisterTransient<ViewFailureView, ViewFailureViewModel>();
        });

        Services = services.BuildServiceProvider();

        // Platform-specific wiring: the shared MenuViewModel only raises
        // OpenSecondWindowRequested; creating the actual Window is the host's job.
        Services.GetRequiredService<MenuViewModel>().OpenSecondWindowRequested +=
            () => new SecondWindow().Show();

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
