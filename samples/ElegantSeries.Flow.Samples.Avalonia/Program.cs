using Avalonia;

namespace ElegantSeries.Flow.Samples.Avalonia;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // Headless smoke test for CI: verifies the AOT-published binary actually
        // runs (DI + navigation pipeline) without initializing any UI.
        if (args.Contains("--smoke-test"))
            return await SmokeTest.RunAsync();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
