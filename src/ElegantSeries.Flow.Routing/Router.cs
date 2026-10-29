using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Routing;

/// <summary>
/// Navigates by URI: matches the URI against a
/// <see cref="RouteTable"/> and performs the navigation the matched
/// route was registered with.
/// </summary>
public interface IRouter
{
    /// <summary>
    /// Navigates to the route matching <paramref name="uri"/>.
    /// </summary>
    /// <param name="uri">A path-and-query URI such as
    /// <c>"orders/42?expand=items"</c>; a full URI
    /// (<c>"app://orders/42"</c>) is also accepted — the
    /// <c>scheme://</c> prefix is stripped and everything after it is
    /// treated as the path (a desktop deep link's authority carries
    /// no routing meaning), and any fragment is ignored. The empty
    /// string and <c>"/"</c> address the root route.</param>
    /// <param name="cancellationToken">Passed through to the
    /// navigation call; matching itself does not consult it.</param>
    /// <returns>The navigation's fate, exactly as the navigation
    /// service reports it: <see langword="true"/> when it was
    /// executed, <see langword="false"/> when it was vetoed,
    /// superseded, or discarded (see the navigation service's own
    /// documentation).</returns>
    /// <exception cref="RouteNotFoundException">No registered route
    /// matches <paramref name="uri"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/>
    /// is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is
    /// whitespace-only (the empty string is valid: it addresses the
    /// root route).</exception>
    Task<bool> NavigateAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="NavigateAsync"/>, except that a URI no route
    /// matches returns <see langword="false"/> instead of throwing
    /// <see cref="RouteNotFoundException"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/>
    /// is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is
    /// whitespace-only.</exception>
    Task<bool> TryNavigateAsync(string uri, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="IRouter"/>: an immutable
/// <see cref="RouteTable"/> plus the navigation service the matched
/// routes navigate through. Holds no resources of its own and never
/// disposes the navigation service; safe to call concurrently (the
/// navigation service serializes the actual navigations).
/// </summary>
public sealed class Router : IRouter
{
    private readonly INavigationService _navigation;
    private readonly RouteTable _routes;

    /// <summary>Creates a router over a navigation service and a
    /// built route table.</summary>
    public Router(INavigationService navigation, RouteTable routes)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(routes);
        _navigation = navigation;
        _routes = routes;
    }

    /// <inheritdoc />
    public async Task<bool> NavigateAsync(string uri, CancellationToken cancellationToken = default)
    {
        var (entry, match) = Match(uri)
            ?? throw new RouteNotFoundException(uri);
        return await entry.Invoke(_navigation, match, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryNavigateAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (Match(uri) is not { } found)
        {
            return false;
        }

        return await found.Entry.Invoke(_navigation, found.Match, cancellationToken).ConfigureAwait(false);
    }

    private (RouteEntry Entry, RouteMatch Match)? Match(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Length > 0 && string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException(
                "The URI must not be whitespace-only (the empty string is valid: it addresses the root route).",
                nameof(uri));
        }

        var (segments, query) = RouteUri.Parse(uri);
        foreach (var entry in _routes.Entries)
        {
            if (entry.Segments.Length != segments.Count)
            {
                continue;
            }

            Dictionary<string, string>? values = null;
            var matched = true;
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = entry.Segments[i];
                if (segment.IsPlaceholder)
                {
                    values ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    values[segment.Text] = segments[i];
                }
                else if (!string.Equals(segment.Text, segments[i], StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return (entry, new RouteMatch(
                    uri,
                    segments,
                    values ?? new Dictionary<string, string>(),
                    query));
            }
        }

        return null;
    }
}

/// <summary>URI splitting for the router: fragment removal, optional
/// scheme-prefix stripping, segment and query decoding.</summary>
internal static class RouteUri
{
    public static (IReadOnlyList<string> Segments, IReadOnlyDictionary<string, string> Query) Parse(string uri)
    {
        var s = uri;

        var fragment = s.IndexOf('#');
        if (fragment >= 0)
        {
            s = s[..fragment];
        }

        var query = "";
        var question = s.IndexOf('?');
        if (question >= 0)
        {
            query = s[(question + 1)..];
            s = s[..question];
        }

        // Only a scheme prefix is stripped: when the URI starts
        // with a valid scheme followed by "://", everything after
        // it is path (a desktop deep link's authority carries no
        // routing meaning, so "app://orders/42" routes like
        // "orders/42"). A "://" anywhere else is ordinary text.
        var schemeEnd = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0 && IsValidScheme(s[..schemeEnd]))
        {
            s = s[(schemeEnd + 3)..];
        }

        var rawSegments = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments = new string[rawSegments.Length];
        for (int i = 0; i < rawSegments.Length; i++)
        {
            segments[i] = Uri.UnescapeDataString(rawSegments[i]);
        }

        var queryValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = equals >= 0 ? pair[..equals] : pair;
            var value = equals >= 0 ? pair[(equals + 1)..] : "";
            queryValues[DecodeQueryComponent(key)] = DecodeQueryComponent(value);
        }

        return (segments, queryValues);
    }

    private static string DecodeQueryComponent(string component) =>
        Uri.UnescapeDataString(component.Replace('+', ' '));

    /// <summary>An RFC 3986 scheme: an ASCII letter, then ASCII
    /// letters, digits, <c>+</c>, <c>-</c> or <c>.</c>.</summary>
    private static bool IsValidScheme(string candidate)
    {
        if (candidate.Length == 0 || !IsAsciiLetter(candidate[0]))
        {
            return false;
        }

        for (int i = 1; i < candidate.Length; i++)
        {
            var c = candidate[i];
            if (!(IsAsciiLetter(c) || c is >= '0' and <= '9' or '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
