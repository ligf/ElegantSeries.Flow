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
    /// <see cref="KeepAlive"/> retention reuses the cached instance when
    /// the navigation is recognized as addressed to it; otherwise a new
    /// instance is created and the cached one stays cached until evicted.
    /// </summary>
    /// <remarks>
    /// The cache key is <c>(region, ViewModel type)</c>: each region holds at most one
    /// cached instance per ViewModel type. Caching a second instance of the same
    /// type in the same region evicts and destroys the previously cached one.
    /// Multiple live instances of the same type are supported: instances
    /// covered by other pages stay on the stack, and a forward navigation
    /// recognizes the one it is addressed to — by parameter equality or by
    /// the instance itself through <see cref="INavigationTargetAware"/> —
    /// never re-purposing an instance whose parameter does not match.
    /// </remarks>
    KeepAlive
}
