using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// The main window's startup navigation plan, shared by both sample apps:
/// the menu goes to the sidebar region and the home page to the main region.
/// It lives outside the window constructors so window creation carries no
/// navigation side effects; each platform's bootstrapper runs it after the
/// window's hosts are attached, so the first pages are shown. Failures are
/// observed instead of fire-and-forget.
/// </summary>
public static class StartupNavigation
{
    public static void Run(INavigationService navigation, Action<string> reportError)
    {
        SampleNavigation.Observe(navigation.NavigateToAsync<MenuViewModel>(RegionNames.Sidebar), reportError);
        SampleNavigation.Observe(navigation.NavigateToAsync<HomeViewModel>(RegionNames.MainRegion), reportError);
    }
}
