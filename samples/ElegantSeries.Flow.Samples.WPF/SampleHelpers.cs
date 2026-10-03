namespace ElegantSeries.Flow.Samples.WPF;

/// <summary>
/// Small shared helpers for the sample app.
/// </summary>
internal static class SampleHelpers
{
    /// <summary>
    /// Observes a fire-and-forget navigation task, reporting failures via
    /// <paramref name="reportError"/> instead of leaving an unobserved
    /// task exception.
    /// </summary>
    public static async void ObserveNavigation(Task<bool> navigation, Action<string> reportError)
    {
        try
        {
            await navigation;
        }
        catch (Exception ex)
        {
            reportError($"Navigation failed: {ex.Message}");
        }
    }
}
