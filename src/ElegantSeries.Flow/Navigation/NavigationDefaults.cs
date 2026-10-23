namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Default values for the navigation API.
/// </summary>
public static class NavigationDefaults
{
    /// <summary>
    /// The region name used when no region is specified
    /// (for example, the <c>regionName</c> parameters throughout
    /// <see cref="INavigationService"/>). Region names are trimmed of
    /// surrounding whitespace at the API boundary and compared ordinally
    /// (case-sensitive): <c>"MainRegion"</c> and <c>"mainregion"</c> are
    /// different regions.
    /// </summary>
    public const string DefaultRegionName = "MainRegion";
}
