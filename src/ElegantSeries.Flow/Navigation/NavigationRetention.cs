namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Specifies what happens to a page when it leaves the active position:
/// whether it is destroyed or kept alive in the region's cache for reuse.
/// The retention is recorded on the page's stack entry and governs that
/// page's own departure, whenever it happens.
/// </summary>
public enum NavigationRetention
{
    /// <summary>
    /// The page is destroyed when it leaves: its page scope is disposed,
    /// releasing the ViewModel and its dependency graph.
    /// </summary>
    Destroy,

    /// <summary>
    /// The page is cached for reuse when it leaves, instead of being
    /// destroyed. A later navigation to the same ViewModel type with
    /// <see cref="KeepAlive"/> retention reuses the cached instance.
    /// </summary>
    /// <remarks>
    /// The cache key is <c>(region, ViewModel type)</c>: each region holds at most one
    /// cached instance per ViewModel type. Caching a second instance of the same
    /// type in the same region evicts and destroys the previously cached one.
    /// Navigating to the same type with different parameters reuses the cached
    /// instance (via refresh semantics) rather than creating a second cached copy.
    /// If you need multiple live instances of the same type, use
    /// <see cref="Destroy"/> retention or separate regions.
    /// </remarks>
    KeepAlive
}
