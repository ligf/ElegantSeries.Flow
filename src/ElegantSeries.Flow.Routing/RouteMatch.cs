namespace ElegantSeries.Flow.Routing;

/// <summary>
/// The result of matching a URI against a route pattern: the decoded
/// path segments, the values captured by the pattern's placeholders,
/// and the decoded query values. Handed to the parameter factory of a
/// <c>Map&lt;TViewModel, TParam&gt;</c> registration, which turns it
/// into the navigation parameter — all conversion happens in that
/// delegate, never by reflection.
/// </summary>
public sealed class RouteMatch
{
    internal RouteMatch(
        string rawUri,
        IReadOnlyList<string> segments,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string> query)
    {
        RawUri = rawUri;
        Segments = segments;
        Values = values;
        Query = query;
    }

    /// <summary>The URI exactly as it was passed to the router.</summary>
    public string RawUri { get; }

    /// <summary>The percent-decoded path segments of the URI
    /// (empty for the root route).</summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>Placeholder name → captured segment text
    /// (percent-decoded), for every placeholder in the matched
    /// pattern.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>Query key → decoded value. A key without
    /// <c>=</c> maps to an empty string; when a key repeats, the last
    /// value wins.</summary>
    public IReadOnlyDictionary<string, string> Query { get; }
}
