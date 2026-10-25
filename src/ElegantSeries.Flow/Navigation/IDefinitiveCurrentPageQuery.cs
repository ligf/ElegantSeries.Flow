namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Optional contract implemented by the built-in <see cref="NavigationService"/>:
/// its current-page answer is definitive. A null result means the region
/// definitely has no current page — its stack is empty, or the service has
/// been disposed — and never merely "unknown".
/// </summary>
/// <remarks>
/// <see cref="INavigationService.GetCurrentViewModel"/> alone cannot carry
/// this distinction: a third-party implementation may return null for
/// "unknown", so host controllers must not treat a null answer as proof
/// that a queued navigation event went stale. Through this contract the
/// same null <i>is</i> such proof, and a queued event whose page is no
/// longer current — including there being no current page at all — can
/// be dropped instead of resurrecting a released page's view.
/// </remarks>
internal interface IDefinitiveCurrentPageQuery
{
    /// <summary>
    /// The region's current page, or null when the region definitively has
    /// none. Returns exactly what
    /// <see cref="INavigationService.GetCurrentViewModel"/> returns on the
    /// implementing service; only the guarantee differs.
    /// </summary>
    /// <param name="regionName">The target region.</param>
    INavigationViewModel? GetDefinitiveCurrentViewModel(string regionName);
}
