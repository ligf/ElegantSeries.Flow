using System.Diagnostics.Metrics;
using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Diagnostics;

/// <summary>
/// Observes an <see cref="INavigationService"/> and turns its events into
/// counters on the <c>ElegantSeries.Flow.Diagnostics</c> meter
/// (<see cref="MeterName"/>): <c>navigation.completed</c>,
/// <c>viewmodel.released</c> and <c>cache.cleared</c> carry a
/// <c>region</c> tag; <c>subscriber.errors</c> carries an
/// <c>error.type</c> tag (the exception's full type name) because the
/// underlying event has no region. Read the measurements with any
/// <see cref="MeterListener"/> or OpenTelemetry setup that listens to the
/// meter by name; this package ships no listener and no logging.
/// </summary>
/// <remarks>
/// <para>
/// Counting contract: an event the service raises is counted exactly once
/// per live instance, and counts accumulate from construction. Events the
/// service does not raise are not counted: a navigation vetoed by a guard
/// or superseded by a later request raises no
/// <see cref="INavigationService.RegionNavigated"/> and is not counted,
/// and a cache clear that removed nothing raises no
/// <see cref="INavigationService.RegionCacheCleared"/>. A cache hit and a
/// fresh activation are indistinguishable in
/// <see cref="INavigationService.RegionNavigated"/> and share the
/// <c>navigation.completed</c> counter; this observer deliberately counts
/// only what the public events carry. When the navigation service
/// itself is disposed, it raises
/// <see cref="INavigationService.RegionCacheCleared"/> once for every
/// region that still held stack, cache or pending entries; those
/// shutdown firings are counted in <c>cache.cleared</c> too, including
/// for regions whose cache was empty — provided this observer is still
/// alive at that point. When the observer is owned by a DI container,
/// the container disposes it before the navigation service it depends
/// on, so shutdown firings are not observed on that path; they are
/// counted when the observer's lifetime is managed manually and
/// outlives the service's disposal.
/// </para>
/// <para>
/// The event handlers run on whatever thread the service dispatches on
/// and only record a measurement — they never call back into the
/// navigation service and never throw. Dispose unsubscribes and disposes
/// the meter; it is idempotent, an event racing it is either counted or
/// dropped (never an exception), and no counts are recorded after it
/// returns. This class owns its meter, its counters and its four
/// subscriptions; it never disposes the navigation service. An instance
/// that is never disposed keeps its subscriptions — and keeps counting —
/// for the life of the navigation service.
/// </para>
/// </remarks>
public sealed class NavigationDiagnostics : IDisposable
{
    /// <summary>The name of the meter this observer publishes to.</summary>
    public const string MeterName = "ElegantSeries.Flow.Diagnostics";

    private readonly IDisposable? _regionNavigatedSubscription;
    private readonly IDisposable? _viewModelReleasedSubscription;
    private readonly IDisposable? _regionCacheClearedSubscription;
    private readonly IDisposable? _subscriberErrorSubscription;
    private readonly Meter _meter;
    private readonly Counter<long> _navigationCompleted;
    private readonly Counter<long> _viewModelReleased;
    private readonly Counter<long> _cacheCleared;
    private readonly Counter<long> _subscriberErrors;
    private int _disposed;

    /// <summary>
    /// Creates the observer and subscribes to the service's events
    /// immediately: counting starts with this call.
    /// </summary>
    /// <param name="navigation">The navigation service to observe.</param>
    public NavigationDiagnostics(INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        Meter? meter = null;
        try
        {
            meter = new Meter(MeterName);
            _navigationCompleted = meter.CreateCounter<long>(
                "navigation.completed",
                description: "Navigations that came to rest on a page (RegionNavigated raised).");
            _viewModelReleased = meter.CreateCounter<long>(
                "viewmodel.released",
                description: "Page ViewModels whose ownership was released and whose page scope is being disposed (ViewModelReleased raised).");
            _cacheCleared = meter.CreateCounter<long>(
                "cache.cleared",
                description: "KeepAlive cache clears that removed at least one entry, per region (RegionCacheCleared raised); also raised once per region that held stack, cache or pending entries when the navigation service itself is disposed.");
            _subscriberErrors = meter.CreateCounter<long>(
                "subscriber.errors",
                description: "Exceptions reported through SubscriberError: throwing event subscribers and navigation failures with no awaiting caller.");

            _regionNavigatedSubscription = navigation.SubscribeRegionNavigated(OnRegionNavigated);
            _viewModelReleasedSubscription = navigation.SubscribeViewModelReleased(OnViewModelReleased);
            _regionCacheClearedSubscription = navigation.SubscribeRegionCacheCleared(OnRegionCacheCleared);
            _subscriberErrorSubscription = navigation.SubscribeSubscriberError(OnSubscriberError);

            _meter = meter;
        }
        catch
        {
            // Roll back everything acquired so far: an object whose
            // constructor throws is never disposed, so a subscription
            // already taken would leak (its handlers would keep counting
            // into a meter nobody owns) and an undisposed meter would
            // stay in the process-wide meter registry.
            _regionNavigatedSubscription?.Dispose();
            _viewModelReleasedSubscription?.Dispose();
            _regionCacheClearedSubscription?.Dispose();
            _subscriberErrorSubscription?.Dispose();
            meter?.Dispose();
            throw;
        }
    }

    /// <summary>The meter the four counters are published on.</summary>
    public Meter Meter => _meter;

    /// <summary>
    /// Unsubscribes from the navigation service and disposes the meter.
    /// Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _regionNavigatedSubscription?.Dispose();
        _viewModelReleasedSubscription?.Dispose();
        _regionCacheClearedSubscription?.Dispose();
        _subscriberErrorSubscription?.Dispose();
        _meter.Dispose();
    }

    private void OnRegionNavigated(string region, INavigationViewModel viewModel) =>
        Record(_navigationCompleted, "region", region);

    private void OnViewModelReleased(string region, INavigationViewModel viewModel) =>
        Record(_viewModelReleased, "region", region);

    private void OnRegionCacheCleared(string region) =>
        Record(_cacheCleared, "region", region);

    private void OnSubscriberError(Exception exception) =>
        Record(_subscriberErrors, "error.type", exception.GetType().FullName);

    private void Record(Counter<long> counter, string tagKey, string? tagValue)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            counter.Add(1, new KeyValuePair<string, object?>(tagKey, tagValue));
        }
        catch (Exception)
        {
            // A measurement listener's callback runs synchronously
            // inside Counter.Add and may throw. A diagnostics observer
            // must never fault the navigation it observes — and must
            // not let its own recording failure escape into the
            // service's subscriber-error channel, where it would be
            // counted as a navigation error and contaminate the very
            // metric this class exists to record. Swallow it.
        }
    }
}
