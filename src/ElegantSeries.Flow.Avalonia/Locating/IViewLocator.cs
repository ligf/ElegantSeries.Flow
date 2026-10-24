using ElegantSeries.Flow.Locating;
using ElegantSeries.Flow.Navigation;
using Avalonia.Controls;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

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
    /// parameterless constructor each time <see cref="CreateView(INavigationViewModel)"/> is called.
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
    /// Registers a context-aware view factory for a ViewModel type. The
    /// factory receives the <see cref="ViewCreationContext"/> of the page the
    /// view is created for: the ViewModel, the region it is displayed in, and
    /// the parameter it was most recently activated with. Use this overload
    /// when view creation depends on the region or the navigation parameter.
    /// The factory runs on the UI thread, once per ViewModel instance (views
    /// are cached per instance), so it cannot be used to change the view of a
    /// kept-alive page on re-activation.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type the view is created for.</typeparam>
    /// <param name="viewFactory">Creates the view for a page, given its creation context. Must not return <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewFactory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A view is already registered for <typeparamref name="TViewModel"/>.</exception>
    void RegisterFactory<TViewModel>(Func<ViewCreationContext, Control> viewFactory)
        where TViewModel : INavigationViewModel;

    /// <summary>
    /// Registers a view type for a ViewModel type and registers the ViewModel
    /// as <see cref="ServiceLifetime.Transient"/> in the dependency-injection
    /// container. Equivalent to <see cref="Register{TView, TViewModel}"/> plus
    /// <c>services.AddTransient&lt;TViewModel&gt;()</c>.
    /// </summary>
    /// <typeparam name="TView">The view type. Must have a public parameterless constructor (XAML requirement).</typeparam>
    /// <typeparam name="TViewModel">The ViewModel type.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the locator was constructed directly instead of through
    /// <c>AddElegantSeriesFlowViews(configure)</c> and therefore has no access to the
    /// service collection; or if a view is already registered for
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
    /// <c>AddElegantSeriesFlowViews</c> startup configuration). Registrations added to the
    /// service collection afterwards are silently ignored by Microsoft DI.
    /// </para>
    /// </remarks>
    void RegisterTransient<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : Control, new()
        where TViewModel : class, INavigationViewModel;

    /// <summary>
    /// Registers a view type for a ViewModel type and registers the ViewModel
    /// as <see cref="ServiceLifetime.Singleton"/> in the dependency-injection
    /// container. Equivalent to <see cref="Register{TView, TViewModel}"/> plus
    /// <c>services.AddSingleton&lt;TViewModel&gt;()</c>.
    /// </summary>
    /// <typeparam name="TView">The view type. Must have a public parameterless constructor (XAML requirement).</typeparam>
    /// <typeparam name="TViewModel">The ViewModel type.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the locator was constructed directly instead of through
    /// <c>AddElegantSeriesFlowViews(configure)</c> and therefore has no access to the
    /// service collection; or if a view is already registered for
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
    /// <c>AddElegantSeriesFlowViews</c> startup configuration). Registrations added to the
    /// service collection afterwards are silently ignored by Microsoft DI.
    /// </para>
    /// </remarks>
    void RegisterSingleton<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : Control, new()
        where TViewModel : class, INavigationViewModel;

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
    /// Creates the registered view for the page described by
    /// <paramref name="context"/>. The context is passed through to a factory
    /// registered with <see cref="RegisterFactory{TViewModel}"/>; a factory
    /// registered with <see cref="Register{TViewModel}(Func{TViewModel, Control})"/>
    /// receives only the ViewModel, as before.
    /// </summary>
    /// <param name="context">The creation context; the lookup uses its ViewModel's exact runtime type.</param>
    /// <returns>The created view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No view is registered for the ViewModel's type.</exception>
    Control CreateView(ViewCreationContext context);

    /// <summary>
    /// Determines whether a view is registered for the given ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to check.</typeparam>
    /// <returns><see langword="true"/> if a view is registered; otherwise <see langword="false"/>.</returns>
    bool IsRegistered<TViewModel>()
        where TViewModel : INavigationViewModel;
}
