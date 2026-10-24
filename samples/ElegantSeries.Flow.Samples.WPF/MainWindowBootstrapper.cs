using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF;

/// <summary>
/// Runs the main window's startup sequence: attach the navigation hosts,
/// then run the shared startup navigation plan.
/// </summary>
internal static class MainWindowBootstrapper
{
    public static void Run(MainWindow window, IServiceProvider services, Action<string> reportError)
    {
        var navigation = services.GetRequiredService<INavigationService>();
        window.AttachNavigationHosts(navigation, services.GetRequiredService<IViewLocator>());
        StartupNavigation.Run(navigation, reportError);
    }
}
