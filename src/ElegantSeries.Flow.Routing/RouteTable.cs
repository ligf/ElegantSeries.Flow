using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Routing;

/// <summary>
/// An immutable set of routes, produced by
/// <see cref="RouteTableBuilder.Build"/>. Routes are matched in
/// registration order: the first route whose pattern matches a URI
/// wins. A built table never changes — registering more routes on the
/// builder afterwards does not affect it.
/// </summary>
public sealed class RouteTable
{
    private readonly RouteEntry[] _entries;

    internal RouteTable(RouteEntry[] entries) => _entries = entries;

    /// <summary>The number of registered routes.</summary>
    public int Count => _entries.Length;

    internal RouteEntry[] Entries => _entries;
}

/// <summary>One parsed route: its segment pattern plus the closure
/// that performs the typed navigation for a match.</summary>
internal sealed class RouteEntry
{
    public RouteEntry(
        RouteSegment[] segments,
        Func<INavigationService, RouteMatch, CancellationToken, Task<bool>> invoke)
    {
        Segments = segments;
        Invoke = invoke;
    }

    public RouteSegment[] Segments { get; }

    public Func<INavigationService, RouteMatch, CancellationToken, Task<bool>> Invoke { get; }
}

/// <summary>One pattern segment: either a literal (matched
/// case-insensitively) or a whole-segment placeholder capturing the
/// segment text under <see cref="Text"/> as its name.</summary>
internal readonly struct RouteSegment
{
    private RouteSegment(bool isPlaceholder, string text)
    {
        IsPlaceholder = isPlaceholder;
        Text = text;
    }

    public bool IsPlaceholder { get; }

    public string Text { get; }

    public static RouteSegment Literal(string text) => new(isPlaceholder: false, text);

    public static RouteSegment Placeholder(string name) => new(isPlaceholder: true, name);
}
