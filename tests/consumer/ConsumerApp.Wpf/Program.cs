using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.ViewModels;
using ElegantSeries.Flow.WPF.Extensions;
using ElegantSeries.Flow.WPF.Locating;
using ElegantSeries.Flow.WPF.Views;
using ElegantSeries.Flow.Generated.ConsumerApp.Wpf;

// WPF NuGet consumer smoke test: everything below uses ONLY the
// published packages (restored from the local feed — see
// nuget.config). The Avalonia consumer (../ConsumerApp) exercises
// the shared flow in depth; this app proves the WPF package's own
// surface works from the package: AddElegantSeriesFlowViews with
// the source generator's RegisterAttributedViews against the WPF
// locator, navigation through the packaged core, and view creation
// through the packaged WPF locator. It runs on Windows only; on
// other platforms ../verify.sh builds it as a compile-only check.
//
// Everything runs synchronously on the [STAThread] entry thread:
// creating a WPF view requires an STA thread, and blocking on the
// navigation tasks is safe here because core navigation never
// marshals back to a synchronization context or dispatcher (only
// the WPF NavigationHost does, and it is not used).

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}");
            if (!condition)
            {
                failures++;
            }
        }

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowViews(views => views.RegisterAttributedViews());

        using var provider = services.BuildServiceProvider();
        var navigation = provider.GetRequiredService<INavigationService>();
        var locator = provider.GetRequiredService<IViewLocator>();

        // Navigation against the packaged core assembly.
        Check(navigation.NavigateToAsync<HomeViewModel>().GetAwaiter().GetResult(), "navigate to Home");
        var home = navigation.GetCurrentViewModel();
        Check(home is HomeViewModel, "current is HomeViewModel");

        // The source generator (shipped inside the core package as
        // an analyzer) turned the [ViewFor] attribute into a
        // registration: the packaged WPF locator resolves the right
        // view type and creates it on this STA thread.
        Check(locator.CreateView(home!) is HomeView, "generated registration resolves HomeView for HomeViewModel");

        Console.WriteLine(failures == 0 ? "CONSUMER WPF PASS" : $"CONSUMER WPF FAIL ({failures} checks failed)");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>The WPF consumer's single page.</summary>
public sealed class HomeViewModel : NavigationViewModelBase
{
}

/// <summary>View for <see cref="HomeViewModel"/>, declared for the
/// source generator via <see cref="ViewForAttribute"/> (ViewOnly: the
/// ViewModel's DI registration above stays manual).</summary>
[ViewFor(Lifetime = ViewModelLifetime.ViewOnly)]
public sealed class HomeView : BaseView<HomeViewModel>
{
}
