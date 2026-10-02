namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// Describes a pending navigation for <see cref="INavigationGuardWithContext"/> guards:
/// where it is going, how, and with what parameter.
/// </summary>
public sealed class NavigationGuardContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationGuardContext"/> class.
    /// </summary>
    /// <param name="regionName">The region the navigation targets.</param>
    /// <param name="targetViewModelType">
    /// The ViewModel type being navigated to — or, for back navigation, the type of the
    /// page being returned to.
    /// </param>
    /// <param name="mode">
    /// The navigation mode, or <see langword="null"/> for back navigation
    /// (which has no mode).
    /// </param>
    /// <param name="parameter">
    /// The navigation parameter — or, for back navigation, the parameter the target page
    /// was originally navigated with.
    /// </param>
    /// <param name="isBack"><see langword="true"/> when the pending navigation is a back navigation.</param>
    public NavigationGuardContext(
        string regionName,
        Type targetViewModelType,
        NavigationMode? mode,
        object? parameter,
        bool isBack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        RegionName = regionName;
        TargetViewModelType = targetViewModelType ?? throw new ArgumentNullException(nameof(targetViewModelType));
        Mode = mode;
        Parameter = parameter;
        IsBack = isBack;
    }

    /// <summary>Gets the region the navigation targets.</summary>
    public string RegionName { get; }

    /// <summary>
    /// Gets the ViewModel type being navigated to — or, for back navigation, the type of
    /// the page being returned to.
    /// </summary>
    public Type TargetViewModelType { get; }

    /// <summary>
    /// Gets the navigation mode, or <see langword="null"/> for back navigation
    /// (which has no mode).
    /// </summary>
    public NavigationMode? Mode { get; }

    /// <summary>
    /// Gets the navigation parameter — or, for back navigation, the parameter the target
    /// page was originally navigated with.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>Gets whether the pending navigation is a back navigation.</summary>
    public bool IsBack { get; }
}
