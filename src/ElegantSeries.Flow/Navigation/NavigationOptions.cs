namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Configures <see cref="INavigationService"/> behavior.
/// </summary>
public sealed class NavigationOptions
{
    /// <summary>
    /// How long navigation and cache-clearing wait for in-flight async lifecycle
    /// callbacks to finish before releasing their page scopes anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ViewModel that ignores its <see cref="CancellationToken"/> cannot block
    /// navigation forever: after this timeout the scope is released and the hang is
    /// reported via <see cref="INavigationService.SubscriberError"/>. The timed-out
    /// callback may still be running and must not touch disposed dependencies.
    /// </para>
    /// <para>
    /// Default is 5 seconds. Increase it if pages have legitimately slow cleanup
    /// (flushing buffers, closing connections); decrease it for snappier navigation
    /// when ViewModels are known to honor cancellation promptly.
    /// </para>
    /// </remarks>
    public TimeSpan DisposalGateTimeout
    {
        get => _disposalGateTimeout;
        set => _disposalGateTimeout = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DisposalGateTimeout), "DisposalGateTimeout must be positive.");
    }
    private TimeSpan _disposalGateTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long synchronous APIs (<c>ClearCache</c>, <c>ClearAllCache</c>, <c>Dispose</c>)
    /// wait to acquire the navigation lock before throwing <see cref="TimeoutException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Synchronous APIs cannot wait indefinitely: on a UI thread an unbounded wait for
    /// a lock held by a slow navigation would freeze the UI. When the timeout expires,
    /// a <see cref="TimeoutException"/> is thrown whose message points to the async
    /// counterpart.
    /// </para>
    /// <para>
    /// Default is 2 seconds. This is separate from <see cref="DisposalGateTimeout"/>:
    /// acquiring the lock should normally take milliseconds, while page cleanup may
    /// legitimately take much longer.
    /// </para>
    /// </remarks>
    public TimeSpan SyncLockTimeout
    {
        get => _syncLockTimeout;
        set => _syncLockTimeout = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(SyncLockTimeout), "SyncLockTimeout must be positive.");
    }
    private TimeSpan _syncLockTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum number of departures executed within one navigation chain before the
    /// chain is stopped as a probable redirect loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chain is a run of consecutive hops (departures and in-place refreshes) driven by navigation requests
    /// registered from within lifecycle callbacks or leave guards — the requests
    /// that can perpetuate themselves with no external actor in the loop. A
    /// departure driven by a request from outside code restarts the accounting,
    /// as does the region coming to rest. When the limit is reached, the hop that
    /// would exceed it never starts and the stop is reported through
    /// <see cref="INavigationService.SubscriberError"/> as an
    /// <see cref="InvalidOperationException"/> (callback-registered requests have
    /// no awaiting caller to throw to); the current page stays active and intact,
    /// and the service remains usable.
    /// </para>
    /// <para>
    /// Default is 10. Legitimate redirect chains (parameter checks, auth gates) are
    /// short; raise this only for deep wizard-style flows.
    /// </para>
    /// </remarks>
    public int MaxNavigationChainLength
    {
        get => _maxNavigationChainLength;
        set => _maxNavigationChainLength = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxNavigationChainLength), "MaxNavigationChainLength must be positive.");
    }
    private int _maxNavigationChainLength = 10;
}
