namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Specifies how a page is navigated to within a region.
/// </summary>
public enum NavigationMode
{
    /// <summary>Pushes a new page onto the stack. The page is destroyed on back navigation.</summary>
    New,

    /// <summary>
    /// Caches the ViewModel instance for reuse on subsequent navigations.
    /// </summary>
    /// <remarks>
    /// The cache key is <c>(region, ViewModel type)</c>: each region holds at most one
    /// cached instance per ViewModel type. Navigating to the same type with different
    /// parameters reuses the cached instance (via refresh semantics) rather than
    /// creating a second cached copy. If you need multiple live instances of the same
    /// type, use <see cref="New"/> mode or separate regions.
    /// </remarks>
    KeepAlive,

    /// <summary>Replaces the current page without pushing onto the back stack.</summary>
    Replace,

    /// <summary>Clears the entire region stack before pushing the new page.</summary>
    ClearStack
}
