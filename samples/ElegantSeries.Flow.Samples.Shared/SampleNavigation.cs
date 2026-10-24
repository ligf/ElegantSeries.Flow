namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Shared helpers for observing sample navigations.
/// </summary>
public static class SampleNavigation
{
    /// <summary>
    /// Observes a fire-and-forget navigation task, reporting failures via
    /// <paramref name="reportError"/> instead of leaving an unobserved
    /// task exception.
    /// </summary>
    public static async void Observe(Task<bool> navigation, Action<string> reportError)
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
