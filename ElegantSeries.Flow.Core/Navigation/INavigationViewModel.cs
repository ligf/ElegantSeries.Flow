namespace ElegantSeries.Flow.Core.Navigation;

/// <summary>
/// Contract for a ViewModel that the navigation service can manage.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface — usually by deriving from
/// <see cref="ViewModels.NavigationViewModelBase"/> (no MVVM toolkit dependency) or
/// <see cref="ViewModels.BaseViewModel"/> (CommunityToolkit.Mvvm) — to make a ViewModel
/// navigable via <see cref="INavigationService.NavigateToAsync{TViewModel}"/>.
/// </para>
/// <para>
/// The <see cref="Navigation"/> property is assigned by the navigation service only:
/// it is set when the ViewModel becomes the active page and cleared when the page is
/// left. Application code should use the protected navigation helpers on the base
/// classes instead of touching this property.
/// </para>
/// </remarks>
public interface INavigationViewModel
{
    /// <summary>
    /// Gets the navigation service attached to this ViewModel, or <see langword="null"/>
    /// if the ViewModel is not currently the active page.
    /// </summary>
    /// <remarks>
    /// Assigned by the navigation service only; the setter is assembly-visible so the
    /// framework can attach and detach. Do not set this property from application code.
    /// </remarks>
    INavigationService? Navigation { get; internal set; }
}
