namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// Implement this interface on a ViewModel to control whether an incoming
/// navigation reuses this instance when several instances of the same page
/// type may exist.
/// </summary>
/// <remarks>
/// <para>
/// When a forward navigation (push) targets a page type that already has
/// <see cref="NavigationRetention.KeepAlive"/> instances — covered entries on
/// the region's stack, or the entry held in the KeepAlive cache — the service
/// must decide which existing instance, if any, the navigation is addressed
/// to. Candidates are considered nearest to the stack top first. For each
/// candidate whose ViewModel implements this interface, the return value of
/// <see cref="IsNavigationTarget"/> is the final verdict for that candidate:
/// no other rule is consulted for it. A candidate that answers
/// <see langword="true"/> is reactivated with the new parameter; if no
/// candidate is recognized, a new instance is created — an existing instance
/// is never reused merely because it has the same type.
/// </para>
/// <para>
/// For candidates that do not implement this interface, the service compares
/// the parameter the candidate was last activated with against the incoming
/// parameter using <see cref="object.Equals(object, object)"/>. Recognition
/// therefore works without any code when the navigation parameter is a value
/// type, a <see cref="string"/>, or a record (records synthesize value
/// equality), and when both parameters are <see langword="null"/>. A plain
/// reference type without an <see cref="object.Equals(object)"/> override
/// never matches, so every navigation creates a new instance for such pages.
/// </para>
/// <para>
/// This interface is the explicit upgrade for pages whose identity cannot be
/// decided from the stored parameter alone — for example a document page
/// whose file was renamed through "Save As" after it was opened: the stored
/// parameter still names the old file, while the instance itself knows the
/// path it currently represents.
/// </para>
/// </remarks>
public interface INavigationTargetAware
{
    /// <summary>
    /// Asks whether an incoming navigation carrying
    /// <paramref name="parameter"/> is addressed to this instance.
    /// </summary>
    /// <param name="parameter">
    /// The parameter of the incoming navigation. It may be of any type, or
    /// <see langword="null"/>; implementations should answer
    /// <see langword="false"/> for parameters they do not recognize.
    /// </param>
    /// <returns>
    /// <see langword="true"/> to claim the navigation: this instance is
    /// reactivated and receives the parameter through its normal
    /// <see cref="INavigationAware.OnNavigatedTo"/> callback. All other
    /// candidates are left untouched.
    /// </returns>
    /// <remarks>
    /// The method is called while a navigation is being classified, possibly
    /// more than once for the same navigation (the KeepAlive cache check and
    /// the stack scan are separate steps). It must be fast, free of side
    /// effects, and consistent for the same parameter. It must not start or
    /// modify a navigation; read-only service queries (such as
    /// <see cref="INavigationService.GetNavigationStack"/>) are safe to call.
    /// Note that a candidate that is covered by another page or held in the
    /// KeepAlive cache has no live
    /// <see cref="INavigationViewModel.Navigation"/> reference — it is
    /// detached while the page is inactive — so implementations must decide
    /// from their own state (or services captured while active), not from
    /// the <see cref="INavigationViewModel.Navigation"/> property.
    /// Classification is not serialized with page disposal: the candidate's
    /// page scope may be disposed concurrently on another thread — by a
    /// concurrent <c>Remove</c>, cache clear, or service disposal — while
    /// this method runs. A service captured from the page scope may
    /// therefore already be disposed when used here.
    /// An exception thrown by the implementation is reported through
    /// <see cref="INavigationService.SubscriberError"/> and the candidate is
    /// treated as not recognized.
    /// </remarks>
    bool IsNavigationTarget(object? parameter);
}
