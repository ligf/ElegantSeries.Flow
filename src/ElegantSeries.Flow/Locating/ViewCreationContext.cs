using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Locating;

/// <summary>
/// Describes the page a view is being created for: the ViewModel instance,
/// the region it is displayed in, and the parameter it was most recently
/// activated with. Passed to view factories registered through
/// <c>RegisterFactory</c> on the platform view locators.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RegionName"/> is <see langword="null"/> only when the view is
/// created through the legacy <c>CreateView(INavigationViewModel)</c> path,
/// which has no region information. Views created by a navigation host
/// always carry the host's region.
/// </para>
/// <para>
/// The context deliberately carries no <see cref="IServiceProvider"/> and no
/// page scope: a page's scope is owned by the navigation service and is
/// disposed when the page leaves, so handing it to a view factory would let
/// the factory resolve services after the page is gone. A view that needs
/// services should capture the application's root provider in the factory
/// closure, or receive them through its ViewModel (which is resolved from
/// the page scope).
/// </para>
/// <para>
/// A view is created once per ViewModel instance (the host caches views by
/// instance), so a factory runs on the instance's first activation only;
/// later re-activations of a kept-alive page reuse the cached view and the
/// factory is not consulted again.
/// </para>
/// </remarks>
public sealed class ViewCreationContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewCreationContext"/> class.
    /// </summary>
    /// <param name="viewModel">The ViewModel the view is created for.</param>
    /// <param name="regionName">The region the page is displayed in, or <see langword="null"/> when unknown (legacy creation path).</param>
    /// <param name="parameter">The parameter the page was most recently activated with, or <see langword="null"/> when it was activated without one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public ViewCreationContext(INavigationViewModel viewModel, string? regionName, object? parameter)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        RegionName = regionName;
        Parameter = parameter;
    }

    /// <summary>
    /// Gets the ViewModel the view is created for.
    /// </summary>
    public INavigationViewModel ViewModel { get; }

    /// <summary>
    /// Gets the region the page is displayed in, or <see langword="null"/>
    /// when the view is created through the legacy path without region
    /// information.
    /// </summary>
    public string? RegionName { get; }

    /// <summary>
    /// Gets the parameter the page was most recently activated with, or
    /// <see langword="null"/> when it was activated without one.
    /// </summary>
    public object? Parameter { get; }
}
