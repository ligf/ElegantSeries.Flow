namespace ElegantSeries.Flow.Routing;

/// <summary>
/// Specifies how the ElegantSeries.Flow source generator registers the ViewModel
/// in dependency injection for a <see cref="ViewForAttribute"/> mapping.
/// </summary>
public enum ViewModelLifetime
{
    /// <summary>
    /// The generator emits the equivalent of
    /// <c>IViewLocator.RegisterTransient&lt;TView, TViewModel&gt;()</c>:
    /// view mapping plus a <c>Transient</c> ViewModel registration.
    /// </summary>
    Transient,

    /// <summary>
    /// The generator emits the equivalent of
    /// <c>IViewLocator.RegisterSingleton&lt;TView, TViewModel&gt;()</c>:
    /// view mapping plus a <c>Singleton</c> ViewModel registration.
    /// </summary>
    Singleton,

    /// <summary>
    /// The generator emits only the view mapping (the equivalent of
    /// <c>IViewLocator.Register&lt;TView, TViewModel&gt;()</c>); the ViewModel's
    /// dependency-injection registration stays manual. Use this when the
    /// ViewModel needs custom DI setup (factory, decorators, keyed services).
    /// </summary>
    ViewOnly,
}

/// <summary>
/// Declares which ViewModel a View displays, for AOT-compatible view registration.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is a compile-time contract for the ElegantSeries.Flow source
/// generator: the generator scans Views carrying this attribute and emits the
/// equivalent <c>IViewLocator</c> registration calls, so no runtime reflection
/// or naming conventions are needed. The runtime never scans this attribute —
/// views are resolved solely through explicit <c>IViewLocator</c> registrations
/// (manual or generated).
/// </para>
/// <para>
/// The attribute goes on the <b>View</b> class (not the ViewModel): a ViewModel
/// shared across UI frameworks (e.g. WPF and Avalonia) is displayed by a
/// different View per platform, so each platform's View carries its own
/// declaration.
/// </para>
/// <para>
/// This form carries no ViewModel type: the generator infers it from the
/// View's <c>BaseView&lt;TViewModel&gt;</c> base class, so it requires the
/// View to inherit <c>BaseView&lt;TViewModel&gt;</c>. Views that do not
/// inherit it name their ViewModel explicitly with the generic
/// <see cref="ViewForAttribute{TViewModel}"/> instead.
/// </para>
/// <para>
/// <b>Manual vs. generated registration:</b> both mechanisms are supported, but
/// pick one per ViewModel. View registration is unique per ViewModel type — if
/// a ViewModel is registered both manually and via a generated
/// <see cref="ViewForAttribute"/> mapping (including two mappings pointing at
/// different Views), the duplicate registration throws
/// <see cref="InvalidOperationException"/> at startup.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ViewForAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute. The source generator infers the ViewModel
    /// from the View's <c>BaseView&lt;TViewModel&gt;</c> base class.
    /// </summary>
    public ViewForAttribute()
    {
    }

    /// <summary>
    /// Gets or sets how the source generator registers the ViewModel in
    /// dependency injection. Defaults to <see cref="ViewModelLifetime.Transient"/>.
    /// </summary>
    public ViewModelLifetime Lifetime { get; set; } = ViewModelLifetime.Transient;
}

/// <summary>
/// Declares which ViewModel a View displays, for AOT-compatible view registration,
/// with the ViewModel type given as a generic type argument.
/// </summary>
/// <remarks>
/// <para>
/// This is the strongly typed form of <see cref="ViewForAttribute"/>: use it for
/// Views that do not inherit <c>BaseView&lt;TViewModel&gt;</c> and therefore
/// cannot have their ViewModel inferred. Views that do inherit
/// <c>BaseView&lt;TViewModel&gt;</c> can use the plain
/// <see cref="ViewForAttribute"/> and omit the type entirely.
/// </para>
/// <para>
/// Like <see cref="ViewForAttribute"/>, this attribute is a compile-time
/// contract for the ElegantSeries.Flow source generator only; the runtime
/// never scans it. When both an explicit type argument and a
/// <c>BaseView&lt;TViewModel&gt;</c> base class are present, they must agree —
/// a mismatch is reported as a compile-time error.
/// </para>
/// </remarks>
/// <typeparam name="TViewModel">The ViewModel type displayed by the decorated View.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ViewForAttribute<TViewModel> : Attribute
{
    /// <summary>
    /// Gets or sets how the source generator registers the ViewModel in
    /// dependency injection. Defaults to <see cref="ViewModelLifetime.Transient"/>.
    /// </summary>
    public ViewModelLifetime Lifetime { get; set; } = ViewModelLifetime.Transient;
}
