namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Specifies what a navigation does to the region's stack.
/// </summary>
public enum NavigationOperation
{
    /// <summary>Pushes the new page onto the stack, above the current page.</summary>
    Push,

    /// <summary>Replaces the current page without growing the back stack.</summary>
    Replace,

    /// <summary>Clears the entire region stack before pushing the new page.</summary>
    Reset
}
