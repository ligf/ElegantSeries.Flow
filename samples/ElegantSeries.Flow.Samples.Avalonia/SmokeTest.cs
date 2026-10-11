using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Samples.Avalonia;

/// <summary>
/// Headless smoke test for the AOT-published binary (<c>--smoke-test</c>).
/// Exercises the Core navigation pipeline — DI scopes, Navigate, Refresh,
/// KeepAlive, GoBack — without initializing any Avalonia UI, so it can run on
/// headless CI agents. Exits 0 on success, 1 on any failure. This proves the
/// native AOT executable actually <i>runs</i>, not just that it compiles.
/// </summary>
internal static class SmokeTest
{
    public static async Task<int> RunAsync()
    {
        try
        {
            await using var provider = BuildServiceProvider();
            var navigation = provider.GetRequiredService<INavigationService>();

            // 1. Navigate to a page.
            if (!await navigation.NavigateToAsync<SmokeHomeViewModel>())
                return Fail("NavigateToAsync<SmokeHomeViewModel> returned false.");

            // 2. Refresh the active page (same type + refreshIfActive).
            if (!await navigation.NavigateToAsync<SmokeHomeViewModel>(refreshIfActive: true))
                return Fail("Refresh NavigateToAsync<SmokeHomeViewModel> returned false.");

            // 3. KeepAlive navigation to a second page.
            if (!await navigation.NavigateToAsync<SmokeDetailViewModel>(mode: NavigationMode.KeepAlive))
                return Fail("KeepAlive NavigateToAsync<SmokeDetailViewModel> returned false.");

            if (navigation.GetCurrentViewModel() is not SmokeDetailViewModel)
                return Fail("Expected SmokeDetailViewModel to be current after KeepAlive navigation.");

            // 4. GoBack to the first page.
            if (!await navigation.GoBackAsync())
                return Fail("GoBackAsync returned false.");

            if (navigation.GetCurrentViewModel() is not SmokeHomeViewModel)
                return Fail("Expected SmokeHomeViewModel to be current after GoBackAsync.");

            Console.WriteLine("AOT smoke test passed: Navigate / Refresh / KeepAlive / GoBack OK.");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail($"Unhandled exception: {ex}");
        }

        static int Fail(string message)
        {
            Console.Error.WriteLine($"AOT smoke test FAILED: {message}");
            return 1;
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingletonFlowNavigation();
        services.AddTransient<SmokeHomeViewModel>();
        services.AddTransient<SmokeDetailViewModel>();
        return services.BuildServiceProvider();
    }

    private sealed class SmokeHomeViewModel : NavigationViewModelBase;
    private sealed class SmokeDetailViewModel : NavigationViewModelBase;
}
