using System.Windows;
using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.Core.Navigation;
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

        // ViewModels (transient is the typical lifetime).
        services.AddTransient<HomeViewModel>();
        services.AddTransient<DetailViewModel>();

        // AOT-safe view registration: no runtime reflection.
        services.AddFlowViews(views =>
        {
            views.Register<HomeView, HomeViewModel>();
            views.Register<DetailView, DetailViewModel>();
        });

        Services = services.BuildServiceProvider();

        // Show the first page.
        var navigation = Services.GetRequiredService<INavigationService>();
        _ = navigation.NavigateToAsync<HomeViewModel>();

        base.OnStartup(e);
    }
}
