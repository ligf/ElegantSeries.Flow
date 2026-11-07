using System.Reflection;

namespace ElegantSeries.Flow.Tests.Shared;

/// <summary>
/// The generated-layer reservation the source generator's default
/// namespace derivation relies on, plus the check that enforces it.
/// The generator emits every assembly's view registrations under
/// <c>ElegantSeries.Flow.Generated.&lt;assembly name&gt;</c>, so in every
/// consuming compilation the name <c>Generated</c> directly under the
/// library's root namespace is occupied by a namespace, and the
/// <c>ElegantSeries.Flow.Generated</c> layer holds generated code only.
/// A library type at either spot — one named <c>Generated</c> in the root
/// namespace, or any type inside the <c>Generated</c> layer — would
/// collide with that namespace in consuming compilations (CS0434), and
/// the library's own build would not notice, because the occupation only
/// exists where generated code is compiled. This check is the notice:
/// when it fails, the new type has to be renamed or moved — the
/// generated namespace cannot move instead, because it is part of the
/// library's public contract (it appears in consumers' <c>using</c>
/// directives). Linked into the test projects that can load the library
/// assemblies: Core's tests cover every package except WPF
/// (net10.0-windows, not referenceable from net10.0), whose windows-leg
/// tests run the same check for the WPF assembly.
/// </summary>
public static class GeneratedLayerReservation
{
    /// <summary>
    /// The library assemblies the reservation covers: one entry per
    /// project under <c>src/</c> in the solution. A completeness test in
    /// Core's test project keeps this list in sync with the solution.
    /// </summary>
    public static readonly string[] AllLibraryAssemblies =
    [
        "ElegantSeries.Flow",
        "ElegantSeries.Flow.Mvvm",
        "ElegantSeries.Flow.WPF",
        "ElegantSeries.Flow.Avalonia",
        "ElegantSeries.Flow.Generator",
        "ElegantSeries.Flow.Diagnostics",
        "ElegantSeries.Flow.Routing",
        "ElegantSeries.Flow.Journal",
    ];

    /// <summary>
    /// Asserts that one library assembly keeps the generated layer
    /// reserved: no exported type named <c>Generated</c> in the root
    /// namespace and no exported type inside the
    /// <c>ElegantSeries.Flow.Generated</c> layer. Only exported types are
    /// checked — they are the ones a consuming compilation can see and
    /// collide with; internal types never enter its namespace resolution.
    /// Works on runtime-loaded and metadata-loaded assemblies alike.
    /// </summary>
    public static void AssertReserved(Assembly assembly, string assemblyName)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            Assert.False(
                type.FullName == "ElegantSeries.Flow.Generated",
                $"'{assemblyName}' declares the type 'ElegantSeries.Flow.Generated'. "
                + "That full name is the namespace every consuming compilation's "
                + "generated view registrations live under, so the type collides "
                + "with it there (CS0434). Rename or move the type.");

            var ns = type.Namespace;
            Assert.False(
                ns is not null &&
                (ns == "ElegantSeries.Flow.Generated" ||
                 ns.StartsWith("ElegantSeries.Flow.Generated.", StringComparison.Ordinal)),
                $"'{assemblyName}' declares '{type.FullName}' inside the "
                + "'ElegantSeries.Flow.Generated' layer. That layer is reserved "
                + "for generated registrations named after consumer assemblies, "
                + "and any library type name in it can equal an assembly name "
                + "there. Move the type out of the layer.");
        }
    }
}
