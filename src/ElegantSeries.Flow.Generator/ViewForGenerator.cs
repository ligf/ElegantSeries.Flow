using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ElegantSeries.Flow.Generator;

/// <summary>
/// Generates <c>IViewLocator</c> view registrations from
/// <c>[ViewFor]</c> attributes
/// (<c>ElegantSeries.Flow.Core.Routing.ViewForAttribute</c>).
/// </summary>
/// <remarks>
/// <para>
/// For every class carrying <c>[ViewFor(typeof(TViewModel))]</c>, the generator
/// emits the equivalent of an explicit <c>IViewLocator.Register*</c> call into a
/// generated <c>RegisterAttributedViews</c> extension method. The runtime never
/// scans the attribute; everything is plain generated C#, so the output is
/// Native AOT and trimming safe.
/// </para>
/// <para>
/// The platform is detected from the compilation: exactly one of the
/// <c>ElegantSeries.Flow.Avalonia.Locating.IViewLocator</c> /
/// <c>ElegantSeries.Flow.WPF.Locating.IViewLocator</c> interfaces must be
/// referenced. The <c>Lifetime</c> named argument selects the registration
/// method: <c>Transient</c> (default) → <c>RegisterTransient</c>,
/// <c>Singleton</c> → <c>RegisterSingleton</c>, <c>ViewOnly</c> → mapping-only
/// <c>Register</c>.
/// </para>
/// </remarks>
[Generator]
public sealed class ViewForGenerator : IIncrementalGenerator
{
    private const string ViewForAttributeMetadataName = "ElegantSeries.Flow.Core.Routing.ViewForAttribute";
    private const string AvaloniaLocatorMetadataName = "ElegantSeries.Flow.Avalonia.Locating.IViewLocator";
    private const string WpfLocatorMetadataName = "ElegantSeries.Flow.WPF.Locating.IViewLocator";

    private const string LifetimeTransient = "Transient";
    private const string LifetimeSingleton = "Singleton";
    private const string LifetimeViewOnly = "ViewOnly";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mappings = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ViewForAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetMapping(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        var compilationAndMappings = context.CompilationProvider.Combine(mappings);

        context.RegisterSourceOutput(compilationAndMappings, static (spc, tuple) =>
            Generate(spc, tuple.Left, tuple.Right));
    }

    private sealed class ViewMapping
    {
        public ViewMapping(string viewName, string viewModelName, string registerMethod, Location location, string? invalidLifetime)
        {
            ViewName = viewName;
            ViewModelName = viewModelName;
            RegisterMethod = registerMethod;
            Location = location;
            InvalidLifetime = invalidLifetime;
        }

        public string ViewName { get; }
        public string ViewModelName { get; }
        public string RegisterMethod { get; }
        public Location Location { get; }

        /// <summary>
        /// The unrecognized <c>Lifetime</c> name when the attribute specifies one
        /// outside <c>Transient</c>/<c>Singleton</c>/<c>ViewOnly</c>; otherwise
        /// <see langword="null"/>.
        /// </summary>
        public string? InvalidLifetime { get; }
    }

    private static ViewMapping? GetMapping(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var attribute = ctx.Attributes[0];
        var location = attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? Location.None;

        if (ctx.TargetSymbol is not INamedTypeSymbol viewType)
        {
            return null;
        }

        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Kind != TypedConstantKind.Type ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol viewModelType)
        {
            // Unresolvable ViewModel type argument; the compiler already reports it.
            return null;
        }

        var lifetimeName = GetLifetimeName(attribute);
        string? registerMethod;
        string? invalidLifetime = null;
        switch (lifetimeName)
        {
            case null:
            case LifetimeTransient:
                registerMethod = "RegisterTransient";
                break;
            case LifetimeSingleton:
                registerMethod = "RegisterSingleton";
                break;
            case LifetimeViewOnly:
                registerMethod = "Register";
                break;
            default:
                // A raw integral value with no matching ViewModelLifetime member
                // (only reachable via an explicit cast); reported as FLOWGEN003 below.
                registerMethod = null;
                invalidLifetime = lifetimeName;
                break;
        }

        var format = SymbolDisplayFormat.FullyQualifiedFormat;
        return new ViewMapping(
            viewType.ToDisplayString(format),
            viewModelType.ToDisplayString(format),
            registerMethod ?? string.Empty,
            location,
            invalidLifetime);
    }

    /// <summary>
    /// Resolves the <c>Lifetime</c> named argument to a <c>ViewModelLifetime</c>
    /// member name, or to the raw integral value text when it matches no member.
    /// Returns <see langword="null"/> when the argument is not specified.
    /// </summary>
    private static string? GetLifetimeName(AttributeData attribute)
    {
        // Read the enum member semantically (not from syntax): this resolves both
        // `Lifetime = ViewModelLifetime.Singleton` and integral casts like
        // `Lifetime = (ViewModelLifetime)1` to the member name, and yields the raw
        // value text for values with no matching member (e.g. `(ViewModelLifetime)99`).
        // The boxed values are compared with Equals: Roslyn boxes both sides with
        // the enum's own underlying type, so this works for any underlying type.
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "Lifetime" &&
                namedArgument.Value.Value is { } rawValue &&
                namedArgument.Value.Type is INamedTypeSymbol enumType)
            {
                foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
                {
                    if (member.HasConstantValue &&
                        Equals(rawValue, member.ConstantValue))
                    {
                        return member.Name;
                    }
                }

                return rawValue.ToString();
            }
        }

        return null;
    }

    private static void Generate(
        SourceProductionContext spc,
        Compilation compilation,
        ImmutableArray<ViewMapping> mappings)
    {
        if (mappings.IsEmpty)
        {
            return;
        }

        var avaloniaLocator = compilation.GetTypeByMetadataName(AvaloniaLocatorMetadataName);
        var wpfLocator = compilation.GetTypeByMetadataName(WpfLocatorMetadataName);

        var platformCount = (avaloniaLocator is not null ? 1 : 0) + (wpfLocator is not null ? 1 : 0);
        if (platformCount == 0)
        {
            return;
        }

        if (platformCount > 1)
        {
            spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.AmbiguousPlatform, Location.None));
            return;
        }

        var locatorName = avaloniaLocator is not null ? AvaloniaLocatorMetadataName : WpfLocatorMetadataName;

        // An unrecognized Lifetime (only reachable via an explicit integral cast)
        // is a compile-time error; the runtime would otherwise misbehave silently.
        foreach (var mapping in mappings)
        {
            if (mapping.InvalidLifetime is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidLifetime, mapping.Location, mapping.InvalidLifetime));
                return;
            }
        }

        // Duplicate ViewModel mappings would throw at runtime; fail at compile time instead.
        var seenViewModels = new HashSet<string>();
        foreach (var mapping in mappings)
        {
            if (!seenViewModels.Add(mapping.ViewModelName))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateViewModel, mapping.Location, mapping.ViewModelName));
                return;
            }
        }

        var ordered = mappings
            .OrderBy(m => m.ViewModelName, System.StringComparer.Ordinal)
            .ThenBy(m => m.ViewName, System.StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file was generated by the ElegantSeries.Flow source generator from");
        sb.AppendLine("// [ViewFor] attributes. Do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace ElegantSeries.Flow.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// View registrations generated from <c>[ViewFor]</c> attributes.");
        sb.AppendLine("    /// Call <see cref=\"RegisterAttributedViews\"/> inside");
        sb.AppendLine("    /// <c>services.AddFlowViews(...)</c> to register every attributed view.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ViewForRegistrations");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Registers each view carrying a <c>[ViewFor]</c> attribute on the");
        sb.AppendLine("        /// given view locator, using the registration method selected by the");
        sb.AppendLine("        /// attribute's <c>Lifetime</c>.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"views\">The view locator to register on.</param>");
        sb.Append("        public static void RegisterAttributedViews(this global::");
        sb.Append(locatorName);
        sb.AppendLine(" views)");
        sb.AppendLine("        {");
        foreach (var mapping in ordered)
        {
            sb.Append("            views.");
            sb.Append(mapping.RegisterMethod);
            sb.Append('<');
            sb.Append(mapping.ViewName);
            sb.Append(", ");
            sb.Append(mapping.ViewModelName);
            sb.AppendLine(">();");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("ViewForRegistrations.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor DuplicateViewModel = new DiagnosticDescriptor(
            id: "FLOWGEN001",
            title: "Duplicate ViewFor ViewModel mapping",
            messageFormat: "Multiple views declare [ViewFor] for ViewModel type '{0}'. Each ViewModel type can only be mapped to one view.",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AmbiguousPlatform = new DiagnosticDescriptor(
            id: "FLOWGEN002",
            title: "Ambiguous view locator platform",
            messageFormat: "Both the WPF and Avalonia view locators are referenced; the generator cannot determine which platform to target",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidLifetime = new DiagnosticDescriptor(
            id: "FLOWGEN003",
            title: "Invalid ViewFor lifetime",
            messageFormat: "Unknown Lifetime '{0}' on [ViewFor]; expected Transient, Singleton, or ViewOnly",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
