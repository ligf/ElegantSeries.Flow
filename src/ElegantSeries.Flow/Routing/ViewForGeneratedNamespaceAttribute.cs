namespace ElegantSeries.Flow.Routing;

/// <summary>
/// Specifies the namespace the ElegantSeries.Flow source generator emits this
/// assembly's generated view registrations into, replacing the namespace
/// derived from the assembly name.
/// </summary>
/// <remarks>
/// <para>
/// Without this attribute, the generator emits the <c>ViewForRegistrations</c>
/// class into <c>ElegantSeries.Flow.Generated.&lt;sanitized assembly
/// name&gt;</c> (with a fixed <c>Unknown</c> segment in place of the assembly
/// name when the assembly has no name). Sanitization maps every character
/// that is invalid in an identifier
/// to <c>_</c>, so two assemblies whose names differ only in such characters
/// (for example <c>Contoso-UI</c> and <c>Contoso_UI</c>) derive the same
/// namespace, and an application referencing both cannot tell their
/// registrations apart. Setting this attribute on one of them resolves the
/// collision.
/// </para>
/// <para>
/// The value replaces the derived namespace in full; the assembly name is not
/// prepended. It must be a valid namespace: one or more dot-separated
/// identifiers, each starting with a letter or <c>_</c> and containing only
/// letters, digits and <c>_</c>, and none equal to a C# keyword. The generator
/// validates the value and reports FLOWGEN016 (emitting nothing for the
/// assembly) when it is invalid. The attribute itself only stores the value;
/// it has no runtime effect.
/// </para>
/// <para>
/// Choosing a namespace makes its uniqueness the chooser's responsibility:
/// when two referenced assemblies still resolve to the same registrations
/// type and cannot be distinguished by reference aliases either, the
/// generator reports FLOWGEN015 in the consuming compilation instead of
/// emitting an ambiguous aggregation call.
/// </para>
/// <para>
/// On an assembly with no <c>[ViewFor]</c> views of its own, the value
/// names the namespace of the generated aggregator when the assembly
/// aggregates referenced providers. The value is validated (FLOWGEN016)
/// whenever the compilation actually generates — when it declares
/// attributed views or would emit an aggregation; a compilation that does
/// neither emits nothing and reports nothing, valid value or not.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ViewForGeneratedNamespaceAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with the namespace the generator should emit
    /// this assembly's view registrations into.
    /// </summary>
    /// <param name="namespace">The generated namespace, used verbatim.</param>
    public ViewForGeneratedNamespaceAttribute(string @namespace)
    {
        Namespace = @namespace;
    }

    /// <summary>
    /// Gets the namespace the generator emits this assembly's view
    /// registrations into.
    /// </summary>
    public string Namespace { get; }
}
