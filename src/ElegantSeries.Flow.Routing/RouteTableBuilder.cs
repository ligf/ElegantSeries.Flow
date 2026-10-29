using System.Diagnostics.CodeAnalysis;
using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Routing;

/// <summary>
/// Builds a <see cref="RouteTable"/> from explicit registrations.
/// Registration order is the match priority: when several patterns
/// could match a URI, the one registered first wins.
/// </summary>
/// <remarks>
/// A pattern is a <c>/</c>-separated list of segments; a segment is
/// either a literal (matched case-insensitively) or a whole-segment
/// placeholder <c>{name}</c> that captures the segment's text.
/// Patterns are validated when they are registered — a malformed
/// pattern fails here, at startup, not at navigation time. The empty
/// pattern (<see langword="&quot;&quot;"/> or <c>"/"</c>) registers
/// the root route. The builder itself is not thread-safe; it is a
/// registration-phase object.
/// </remarks>
public sealed class RouteTableBuilder
{
    private readonly List<RouteEntry> _entries = [];

    /// <summary>
    /// Registers a route that navigates to
    /// <typeparamref name="TViewModel"/> without a parameter.
    /// </summary>
    /// <param name="pattern">The route pattern, e.g.
    /// <c>"orders/{id}"</c>.</param>
    /// <param name="region">The region the navigation targets. The URI
    /// never carries a region; it is a property of the
    /// registration.</param>
    /// <param name="operation">The stack operation the navigation
    /// performs.</param>
    /// <param name="retention">What happens to the page when it later
    /// leaves the stack.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/>
    /// is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/>
    /// is malformed (unbalanced braces, an illegal or duplicated
    /// placeholder name, or a placeholder that does not span the whole
    /// segment).</exception>
    public RouteTableBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string pattern,
        string region = NavigationDefaults.DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy)
        where TViewModel : INavigationViewModel
    {
        var segments = ParsePattern(pattern);
        _entries.Add(new RouteEntry(segments,
            (navigation, _, cancellationToken) =>
                navigation.NavigateToAsync<TViewModel>(region, operation, retention, refreshIfActive: false, cancellationToken)));
        return this;
    }

    /// <summary>
    /// Registers a route that navigates to
    /// <typeparamref name="TViewModel"/> with a parameter produced by
    /// <paramref name="parameterFactory"/> from the match (captured
    /// placeholder values and query values). The factory runs once per
    /// navigation, synchronously, before the navigation call; an
    /// exception it throws reaches the navigation caller unwrapped.
    /// </summary>
    /// <param name="pattern">The route pattern, e.g.
    /// <c>"orders/{id}"</c>.</param>
    /// <param name="parameterFactory">Turns a <see cref="RouteMatch"/>
    /// into the navigation parameter. May return null.</param>
    /// <param name="region">The region the navigation targets.</param>
    /// <param name="operation">The stack operation the navigation
    /// performs.</param>
    /// <param name="retention">What happens to the page when it later
    /// leaves the stack.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/>
    /// or <paramref name="parameterFactory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/>
    /// is malformed (see the other overload).</exception>
    public RouteTableBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        string pattern,
        Func<RouteMatch, TParam> parameterFactory,
        string region = NavigationDefaults.DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy)
        where TViewModel : INavigationViewModel
    {
        ArgumentNullException.ThrowIfNull(parameterFactory);
        var segments = ParsePattern(pattern);
        _entries.Add(new RouteEntry(segments,
            (navigation, match, cancellationToken) =>
                navigation.NavigateToAsync<TViewModel, TParam>(parameterFactory(match), region, operation, retention, refreshIfActive: false, cancellationToken)));
        return this;
    }

    /// <summary>
    /// Snapshots the registrations into an immutable
    /// <see cref="RouteTable"/>. The builder stays usable afterwards;
    /// routes registered later do not affect tables already built.
    /// </summary>
    public RouteTable Build() => new([.. _entries]);

    private static RouteSegment[] ParsePattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length > 0 && string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException(
                "Route pattern must not be whitespace (use \"\" or \"/\" for the root route).",
                nameof(pattern));
        }

        var parts = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments = new RouteSegment[parts.Length];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Contains('{') || part.Contains('}'))
            {
                if (part.Length < 3 || part[0] != '{' || part[^1] != '}' ||
                    part.IndexOf('{', 1) >= 0 || part.LastIndexOf('}', part.Length - 2) >= 0)
                {
                    throw new ArgumentException(
                        $"Route pattern segment '{part}' is malformed: a placeholder must span the whole segment as {{name}}.",
                        nameof(pattern));
                }

                var name = part[1..^1];
                if (!IsValidPlaceholderName(name))
                {
                    throw new ArgumentException(
                        $"Route pattern placeholder name '{name}' is not a valid identifier (letter or underscore first, then letters, digits or underscores).",
                        nameof(pattern));
                }

                if (!names.Add(name))
                {
                    throw new ArgumentException(
                        $"Route pattern '{pattern}' uses the placeholder name '{name}' more than once.",
                        nameof(pattern));
                }

                segments[i] = RouteSegment.Placeholder(name);
            }
            else
            {
                segments[i] = RouteSegment.Literal(part);
            }
        }

        return segments;
    }

    private static bool IsValidPlaceholderName(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
