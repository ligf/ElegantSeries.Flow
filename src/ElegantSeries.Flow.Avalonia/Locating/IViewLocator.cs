using ElegantSeries.Flow.Core.Navigation;
using Avalonia.Controls;

namespace ElegantSeries.Flow.Avalonia.Locating;

/// <summary>
/// Maps ViewModel types to their corresponding Avalonia views.
/// </summary>
/// <remarks>
/// <para>
/// Registration is explicit and compile-time generic: no reflection, no source
/// generators, and no runtime type scanning are involved, so the locator is
/// Native AOT and trimming safe. Lookup is a dictionary keyed by the exact
/// runtime type of the ViewModel (<see cref="object.GetType"/>): a derived
/// ViewModel type does not inherit its base type's registration.
/// </para>
/// <para>
/// Register all view/view-model pairs once at application startup, before any
/// navigation occurs. Registration is thread-safe, but registering from multiple
/// threads concurrently is discouraged; prefer single-threaded startup registration.
/// </para>
/// </remarks>
public interface IViewLocator
{
    /// <summary>
    /// Registers a view type for a ViewModel type. The view is created with its
    /// parameterless constructor each time <see cref="CreateView"/> is called.
    /// </summary>
    /// <typeparam name="TView">The view type. Must have a public parameterless constructor (XAML requirement).</typeparam>
    /// <typeparam name="TViewModel">The ViewModel type.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a view is already registered for <typeparamref name="TViewModel"/>.
    /// </exception>
    /// <remarks>
    /// The view instance is created via <c>new()</c> — no <see cref="Activator"/>
    /// or other reflection-based instantiation is used.
    /// </remarks>
    void Register<TView, TViewModel>()
        where TView : Control, new()
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Registers a view factory for a ViewModel type. Use this overload when the
    /// view requires constructor arguments or other custom construction logic.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type.</typeparam>
    /// <param name="viewFactory">
    /// Factory that creates the view for the given ViewModel instance.
    /// Must not return <see langword="null"/>. It runs on the UI thread and must
    /// not have blocking side effects.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="viewFactory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a view is already registered for <typeparamref name="TViewModel"/>.
    /// </exception>
    void Register<TViewModel>(Func<TViewModel, Control> viewFactory)
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Creates the view registered for the runtime type of <paramref name="viewModel"/>.
    /// </summary>
    /// <param name="viewModel">The ViewModel instance to create a view for.</param>
    /// <returns>A new view instance. The caller is responsible for assigning its <c>DataContext</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no view is registered for the ViewModel's runtime type, or if the
    /// registered factory returned <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Exceptions thrown by a user-supplied <c>viewFactory</c> propagate to the caller;
    /// the locator itself keeps no partial state.
    /// </remarks>
    Control CreateView(INavigationViewModel viewModel);

    /// <summary>
    /// Determines whether a view is registered for the given ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to check.</typeparam>
    /// <returns><see langword="true"/> if a view is registered; otherwise <see langword="false"/>.</returns>
    bool IsRegistered<TViewModel>()
        where TViewModel : INavigationViewModel;
}
