using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ElegantSeries.Flow.Avalonia.Extensions;
using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaSample.ViewModels;
using AvaloniaSample.Views;

namespace AvaloniaSample;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
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
