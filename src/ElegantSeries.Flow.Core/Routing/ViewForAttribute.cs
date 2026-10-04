using System.Diagnostics.CodeAnalysis;

namespace ElegantSeries.Flow.Core.Routing;

/// <summary>
/// Declares which ViewModel a View displays, for AOT-compatible view registration.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is a compile-time contract for the ElegantSeries.Flow source
/// generator (future): the generator scans Views carrying this attribute and emits
/// the equivalent of <c>IViewLocator.Register&lt;TView, TViewModel&gt;()</c> calls,
/// so no runtime reflection or naming conventions are needed. The runtime never
/// scans this attribute — views are resolved solely through explicit
/// <c>IViewLocator</c> registrations.
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
}
