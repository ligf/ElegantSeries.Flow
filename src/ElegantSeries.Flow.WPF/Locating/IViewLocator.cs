using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.WPF.Locating;

/// <summary>
/// Maps ViewModel types to their WPF views without reflection, so the mapping
/// stays trimming- and AOT-safe.
/// </summary>
/// <remarks>
/// <para>
/// Registration is explicit and generic: <c>Register&lt;MyView, MyViewModel&gt;()</c>.
/// Views are resolved by exact runtime type via <see cref="object.GetType"/>;
/// no attributes are scanned and no source generator is required.
/// </para>
/// <para>
/// Implementations are thread-safe. Registration is expected to happen once at
/// application startup (single-threaded); <see cref="CreateView"/> may be called
/// concurrently from the UI thread.
/// </para>
/// </remarks>
public interface IViewLocator
{
    /// <summary>
    /// Registers a view type for a ViewModel type. The view is created with its
    /// parameterless constructor on the UI thread when first needed.
    /// </summary>
    /// <typeparam name="TView">The view type. Must derive from <see cref="FrameworkElement"/> and have a public parameterless constructor (a XAML requirement).</typeparam>
    /// <typeparam name="TViewModel">The ViewModel type the view is created for.</typeparam>
    /// <exception cref="InvalidOperationException">A view is already registered for <typeparamref name="TViewModel"/>.</exception>
    void Register<TView, TViewModel>()
        where TView : FrameworkElement, new()
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Registers a view factory for a ViewModel type. Use this overload when the
    /// view needs constructor arguments or any other custom creation logic.
    /// The factory runs on the UI thread.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type the view is created for.</typeparam>
    /// <param name="viewFactory">Creates the view for a given ViewModel instance. Must not return <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewFactory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A view is already registered for <typeparamref name="TViewModel"/>.</exception>
    void Register<TViewModel>(Func<TViewModel, FrameworkElement> viewFactory)
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Registers a view type for a ViewModel type and registers the ViewModel
    /// as <see cref="ServiceLifetime.Transient"/> in the dependency-injection
    /// container. Equivalent to <see cref="Register{TView, TViewModel}"/> plus
    /// <c>services.AddTransient&lt;TViewModel&gt;()</c>.
    /// </summary>
    /// <typeparam name="TView">The view type. Must derive from <see cref="FrameworkElement"/> and have a public parameterless constructor (a XAML requirement).</typeparam>
    /// <typeparam name="TViewModel">The ViewModel type the view is created for.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// The locator was constructed directly instead of through
    /// <c>AddFlowViews(configure)</c> and therefore has no access to the
    /// service collection; or a view is already registered for
    /// <typeparamref name="TViewModel"/>.
    /// </exception>
    /// <remarks>
    /// The lifetime applies to the ViewModel's service registration only.
    /// Views are always created per ViewModel instance (and cached while the
    /// instance is alive), independently of this lifetime.
    /// <para>
    /// There is intentionally no <c>RegisterScoped</c>: the navigation service
    /// creates a new <see cref="IServiceScope"/> per page, so a scoped
    /// ViewModel would behave exactly like a transient one. Use scoped
    /// lifetime for the ViewModel's <i>dependencies</i> (DbContext, drafts,
    /// per-page caches) instead.
    /// </para>
    /// <para>
    /// Must be called before the service provider is built (i.e. during
    /// <c>AddFlowViews</c> startup configuration). Registrations added to the
    /// service collection afterwards are silently ignored by Microsoft DI.
    /// </para>
    /// </remarks>
    void RegisterTransient<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : FrameworkElement, new()
        where TViewModel : class, INavigationViewModel;

    /// <summary>
    /// Registers a view type for a ViewModel type and registers the ViewModel
    /// as <see cref="ServiceLifetime.Singleton"/> in the dependency-injection
    /// container. Equivalent to <see cref="Register{TView, TViewModel}"/> plus
    /// <c>services.AddSingleton&lt;TViewModel&gt;()</c>.
    /// </summary>
    /// <typeparam name="TView">The view type. Must derive from <see cref="FrameworkElement"/> and have a public parameterless constructor (a XAML requirement).</typeparam>
    /// <typeparam name="TViewModel">The ViewModel type the view is created for.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// The locator was constructed directly instead of through
    /// <c>AddFlowViews(configure)</c> and therefore has no access to the
    /// service collection; or a view is already registered for
    /// <typeparamref name="TViewModel"/>.
    /// </exception>
    /// <remarks>
    /// The lifetime applies to the ViewModel's service registration only.
    /// Views are always created per ViewModel instance (and cached while the
    /// instance is alive), independently of this lifetime.
    /// <para>
    /// A singleton ViewModel keeps its state across all navigations. Its
    /// constructor must not depend on scoped services.
    /// </para>
    /// <para>
    /// Must be called before the service provider is built (i.e. during
    /// <c>AddFlowViews</c> startup configuration). Registrations added to the
    /// service collection afterwards are silently ignored by Microsoft DI.
    /// </para>
    /// </remarks>
    void RegisterSingleton<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : FrameworkElement, new()
        where TViewModel : class, INavigationViewModel;

    /// <summary>
    /// Creates the registered view for a ViewModel instance.
    /// </summary>
    /// <param name="viewModel">The ViewModel instance to create a view for. The lookup uses its exact runtime type.</param>
    /// <returns>The created view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No view is registered for the ViewModel's type.</exception>
    FrameworkElement CreateView(INavigationViewModel viewModel);

    /// <summary>
    /// Determines whether a view is registered for the given ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to check.</typeparam>
    /// <returns><see langword="true"/> if a view is registered; otherwise <see langword="false"/>.</returns>
    bool IsRegistered<TViewModel>()
        where TViewModel : INavigationViewModel;
}
