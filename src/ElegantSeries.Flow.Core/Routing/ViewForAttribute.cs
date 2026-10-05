using System.Diagnostics.CodeAnalysis;

namespace ElegantSeries.Flow.Core.Routing;

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
/// <b>Manual vs. generated registration:</b> both mechanisms are supported, but
/// pick one per ViewModel. View registration is unique per ViewModel type — if
/// a ViewModel is registered both manually and via a generated
/// <see cref="ViewForAttribute"/> mapping (including two mappings pointing at
/// different Views), the duplicate registration throws
/// <see cref="InvalidOperationException"/> at startup.
/// </para>
/// </remarks>
/// <param name="viewModelType">The ViewModel type displayed by the decorated View.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ViewForAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type viewModelType) : Attribute
{
    /// <summary>
    /// Gets the ViewModel type displayed by the decorated View.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type ViewModelType { get; } = viewModelType ?? throw new ArgumentNullException(nameof(viewModelType));

    /// <summary>
    /// Gets or sets how the source generator registers the ViewModel in
    /// dependency injection. Defaults to <see cref="ViewModelLifetime.Transient"/>.
    /// </summary>
    public ViewModelLifetime Lifetime { get; set; } = ViewModelLifetime.Transient;
}
