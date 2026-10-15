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
    public TimeSpan DisposalGateTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
