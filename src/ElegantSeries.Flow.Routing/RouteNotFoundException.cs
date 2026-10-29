namespace ElegantSeries.Flow.Routing;

/// <summary>
/// Thrown by <see cref="IRouter.NavigateAsync"/> when no registered
/// route matches the URI. (<see cref="IRouter.TryNavigateAsync"/>
/// returns <see langword="false"/> in the same situation instead.)
/// </summary>
public class RouteNotFoundException : Exception
{
    /// <summary>Creates the exception for the unmatched URI.</summary>
    /// <param name="uri">The URI no route matched, exactly as passed
    /// to the router.</param>
    public RouteNotFoundException(string uri)
        : base($"No route matches '{uri}'.")
    {
        Uri = uri;
    }

    /// <summary>The URI no route matched, exactly as passed to the
    /// router.</summary>
    public string Uri { get; }
}
