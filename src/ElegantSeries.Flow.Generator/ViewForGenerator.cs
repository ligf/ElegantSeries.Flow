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
/// (<c>ElegantSeries.Flow.Routing.ViewForAttribute</c>).
/// </summary>
/// <remarks>
/// <para>
/// For every class carrying <c>[ViewFor]</c>, the generator emits the equivalent
/// of an explicit <c>IViewLocator.Register*</c> call into a generated
/// <c>RegisterAttributedViews</c> extension method. The ViewModel type is either
/// given explicitly (<c>[ViewFor(typeof(TViewModel))]</c>) or inferred from the
/// view's <c>BaseView&lt;TViewModel&gt;</c> base class. The runtime never scans
/// the attribute; everything is plain generated C#, so the output is Native AOT
/// and trimming safe.
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
    private const string ViewForAttributeMetadataName = "ElegantSeries.Flow.Routing.ViewForAttribute";

    // Platform namespace roots. The locator and BaseView<TViewModel> namespaces
    // are derived from these, so supporting a new platform only touches this table.
    private const string AvaloniaNamespaceRoot = "ElegantSeries.Flow.Avalonia";
    private const string WpfNamespaceRoot = "ElegantSeries.Flow.WPF";

    private const string AvaloniaLocatorMetadataName = AvaloniaNamespaceRoot + ".Locating.IViewLocator";
    private const string WpfLocatorMetadataName = WpfNamespaceRoot + ".Locating.IViewLocator";

    private const string AvaloniaViewsNamespace = "global::" + AvaloniaNamespaceRoot + ".Views";
    private const string WpfViewsNamespace = "global::" + WpfNamespaceRoot + ".Views";

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
        public ViewMapping(string viewName, string viewModelName, string registerMethod, Location location, string? invalidLifetime, bool cannotInferViewModel, string? conflictingBaseViewModel, bool unresolvableViewModelType, bool isOpenGenericView, bool isOpenGenericViewModel, string? viewModelNotImplementingInterface, ImmutableArray<string> viewConstraintViolations, string? viewModelNotReferenceType)
        {
            ViewName = viewName;
            ViewModelName = viewModelName;
            RegisterMethod = registerMethod;
            Location = location;
            InvalidLifetime = invalidLifetime;
            CannotInferViewModel = cannotInferViewModel;
            ConflictingBaseViewModel = conflictingBaseViewModel;
            UnresolvableViewModelType = unresolvableViewModelType;
            IsOpenGenericView = isOpenGenericView;
            IsOpenGenericViewModel = isOpenGenericViewModel;
            ViewModelNotImplementingInterface = viewModelNotImplementingInterface;
            ViewConstraintViolations = viewConstraintViolations;
            ViewModelNotReferenceType = viewModelNotReferenceType;
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

        /// <summary>
        /// True when <c>[ViewFor]</c> omits the ViewModel type and it cannot be
        /// inferred from a <c>BaseView&lt;TViewModel&gt;</c> base class (FLOWGEN004).
        /// </summary>
        public bool CannotInferViewModel { get; }

        /// <summary>
        /// The <c>BaseView&lt;T&gt;</c> type argument when it conflicts with an
        /// explicitly specified ViewModel type (FLOWGEN005); otherwise
        /// <see langword="null"/>.
        /// </summary>
        public string? ConflictingBaseViewModel { get; }

        /// <summary>
        /// True when <c>[ViewFor]</c> specifies a ViewModel type argument that
        /// could not be resolved to a type, and the compiler did not already
        /// report it (FLOWGEN006).
        /// </summary>
        public bool UnresolvableViewModelType { get; }

        /// <summary>
        /// True when the view is an open generic type (e.g. <c>GenericView&lt;T&gt;</c>),
        /// which cannot be registered (FLOWGEN008).
        /// </summary>
        public bool IsOpenGenericView { get; }

        /// <summary>
        /// True when the explicitly specified ViewModel is an open generic type
        /// (e.g. <c>DetailViewModel&lt;&gt;</c>), which cannot be registered (FLOWGEN009).
        /// </summary>
        public bool IsOpenGenericViewModel { get; }

        /// <summary>
        /// The ViewModel type name when it does not implement INavigationViewModel
        /// (FLOWGEN010); otherwise <see langword="null"/>.
        /// </summary>
        public string? ViewModelNotImplementingInterface { get; }

        /// <summary>
        /// One entry per view-side requirement of the platform view locator that
        /// the view type fails (FLOWGEN011): platform base-type derivation,
        /// non-abstract, public parameterless constructor, accessibility from
        /// the generated registration code. Empty when the view can be registered.
        /// </summary>
        public ImmutableArray<string> ViewConstraintViolations { get; }

        /// <summary>
        /// The ViewModel type name when it is a value type registered via
        /// RegisterTransient/RegisterSingleton, whose ViewModel type parameter
        /// requires a reference type (FLOWGEN012); otherwise
        /// <see langword="null"/>. A value-type ViewModel is legal with
        /// ViewOnly (Register has no reference-type requirement).
        /// </summary>
        public string? ViewModelNotReferenceType { get; }
    }

    private static ViewMapping? GetMapping(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var attribute = ctx.Attributes[0];
        var location = attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? Location.None;

        if (ctx.TargetSymbol is not INamedTypeSymbol viewType)
        {
            return null;
        }

        var format = SymbolDisplayFormat.FullyQualifiedFormat;
        var viewName = viewType.ToDisplayString(format);

        // The ViewModel type is either explicit ([ViewFor(typeof(VM))]) or inferred
        // from the view's BaseView<TViewModel> base class ([ViewFor]).
        var baseViewModel = FindBaseViewModel(viewType);
        INamedTypeSymbol? explicitViewModelSymbol = null;
        string? viewModelName = null;
        var cannotInfer = false;
        string? conflictingBaseViewModel = null;
        var unresolvableViewModelType = false;
        var isOpenGenericViewModel = false;
        string? viewModelNotImplementingInterface = null;

        if (attribute.ConstructorArguments.Length == 0)
        {
            if (baseViewModel is not null)
            {
                viewModelName = baseViewModel.ToDisplayString(format);
            }
            else
            {
                cannotInfer = true;
            }
        }
        else if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol explicitViewModel)
        {
            explicitViewModelSymbol = explicitViewModel;
            viewModelName = explicitViewModel.ToDisplayString(format);
            // Open generic ViewModels (e.g. DetailViewModel<>) cannot be registered:
            // the generated code would reference an undefined type parameter.
            // typeof(X<>) yields an unbound generic type.
            isOpenGenericViewModel = explicitViewModel.IsGenericType &&
                (explicitViewModel.IsUnboundGenericType ||
                 explicitViewModel.TypeArguments.Any(a => a is ITypeParameterSymbol));
            if (!isOpenGenericViewModel)
            {
                // The ViewModel must implement INavigationViewModel; otherwise the
                // generated Register call violates its generic constraint.
                // Skip error types (e.g. typo'd names): the compiler already reports them.
                if (explicitViewModel is { TypeKind: not TypeKind.Error })
                {
                    var navigationViewModel = ctx.SemanticModel.Compilation
                        .GetTypeByMetadataName("ElegantSeries.Flow.Navigation.INavigationViewModel");
                    if (navigationViewModel is not null &&
                        !explicitViewModel.AllInterfaces.Contains(navigationViewModel, SymbolEqualityComparer.Default))
                    {
                        viewModelNotImplementingInterface = viewModelName;
                    }
                }
            }
            if (baseViewModel is not null &&
                !SymbolEqualityComparer.Default.Equals(baseViewModel, explicitViewModel))
            {
                conflictingBaseViewModel = baseViewModel.ToDisplayString(format);
            }
        }
        else if (attribute.ConstructorArguments[0].Kind != TypedConstantKind.Error)
        {
            // The type argument could not be resolved (e.g. an explicit null),
            // and the compiler did not already report it: surface FLOWGEN006
            // instead of silently skipping the view.
            unresolvableViewModelType = true;
        }
        else
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

        // Open generic views (e.g. GenericView<T>) cannot be registered: the generated
        // code would reference an undefined type parameter.
        bool isOpenGenericView = viewType.IsGenericType &&
            (viewType.IsUnboundGenericType ||
             viewType.TypeArguments.Any(a => a is ITypeParameterSymbol));

        // An open generic view is already rejected with FLOWGEN008. A
        // parameterless [ViewFor] on a view like GenericView<T> : BaseView<T>
        // would also raise FLOWGEN004, because the BaseView argument is the
        // view's own type parameter and no ViewModel can be inferred from
        // it — but that diagnostic's message claims the view does not
        // inherit BaseView<TViewModel>, which is false here, and a second
        // error adds nothing: the view cannot be registered either way.
        if (isOpenGenericView)
        {
            cannotInfer = false;
        }

        // View-side requirements of the generated Register* call (FLOWGEN011).
        // The locator's TView parameter requires, on both platforms, derivation
        // from the platform view base type plus a public parameterless
        // constructor (the new() constraint also excludes abstract types), and
        // the generated code must be able to name the view type. Without these
        // checks the failure surfaces as a raw compiler error (CS0310/CS0311/
        // CS0122) inside the generated file. Skipped for open generic views:
        // FLOWGEN008 already rejects them, and constraint questions about an
        // unbound type have no meaningful answer.
        var viewConstraintViolations = ImmutableArray.CreateBuilder<string>();
        if (!isOpenGenericView)
        {
            var compilation = ctx.SemanticModel.Compilation;

            // The platform is identified by which Flow locator the compilation
            // references (mirrors Generate); the base type follows from it.
            // When neither locator is referenced, Generate reports nothing at
            // all, so skipping the derivation check loses no diagnostic; when
            // both are referenced, FLOWGEN002 suppresses per-mapping reports.
            INamedTypeSymbol? platformBaseType = null;
            string? platformBaseTypeName = null;
            if (compilation.GetTypeByMetadataName(AvaloniaLocatorMetadataName) is not null)
            {
                platformBaseType = compilation.GetTypeByMetadataName("Avalonia.Controls.Control");
                platformBaseTypeName = "Avalonia.Controls.Control";
            }
            else if (compilation.GetTypeByMetadataName(WpfLocatorMetadataName) is not null)
            {
                platformBaseType = compilation.GetTypeByMetadataName("System.Windows.FrameworkElement");
                platformBaseTypeName = "System.Windows.FrameworkElement";
            }

            // Skipped when the base chain contains an error type (a typo'd or
            // missing base class): whether the view derives from the platform
            // base is then unknowable — the intended base may well derive —
            // and the compiler already reports the user's own error.
            if (platformBaseType is not null &&
                !BaseChainContainsError(viewType) &&
                !DerivesFrom(viewType, platformBaseType))
            {
                viewConstraintViolations.Add($"it does not derive from '{platformBaseTypeName}'");
            }

            // Static classes group with abstract ones (neither can be
            // instantiated); note IsAbstract is false for a static class
            // symbol, so IsStatic must be tested explicitly.
            if (viewType.IsAbstract || viewType.IsStatic)
            {
                viewConstraintViolations.Add("it is abstract or static and cannot be instantiated");
            }

            // Skipped for abstract/static views: their implicit constructor
            // is protected (or absent), so the check would stack a derivative
            // "no public parameterless constructor" on top of the abstract
            // report even though abstractness alone already explains the
            // new() failure.
            if (!viewType.IsAbstract && !viewType.IsStatic &&
                !viewType.InstanceConstructors.Any(c =>
                    c.Parameters.Length == 0 &&
                    c.DeclaredAccessibility == Accessibility.Public))
            {
                viewConstraintViolations.Add("it does not declare a public parameterless constructor");
            }

            if (!IsAccessibleFromGeneratedCode(viewType))
            {
                viewConstraintViolations.Add("it is not accessible from the generated registration code (the view and any containing types must be public or internal)");
            }

            if (IsFileLocal(viewType))
            {
                viewConstraintViolations.Add("it is file-local (or nested in a file-local type) and cannot be referenced from the generated registration code");
            }

            if (HasGenericContainingType(viewType))
            {
                viewConstraintViolations.Add("it is nested in a generic type and cannot be named by the generated registration code");
            }
        }

        // RegisterTransient/RegisterSingleton require a reference-type ViewModel
        // (FLOWGEN012); Register (ViewOnly) does not, so a value-type ViewModel
        // is legal there. Only the explicit form can reach this: an inferred
        // ViewModel comes from BaseView<TViewModel>, whose own constraint
        // already requires a reference type. Error types are skipped (the
        // compiler already reports them), as are open generic ViewModels
        // (FLOWGEN009).
        string? viewModelNotReferenceType = null;
        if (explicitViewModelSymbol is { IsValueType: true, TypeKind: not TypeKind.Error } &&
            !isOpenGenericViewModel &&
            registerMethod is "RegisterTransient" or "RegisterSingleton")
        {
            viewModelNotReferenceType = viewModelName;
        }

        return new ViewMapping(
            viewName,
            viewModelName ?? string.Empty,
            registerMethod ?? string.Empty,
            location,
            invalidLifetime,
            cannotInfer,
            conflictingBaseViewModel,
            unresolvableViewModelType,
            isOpenGenericView,
            isOpenGenericViewModel,
            viewModelNotImplementingInterface,
            viewConstraintViolations.ToImmutable(),
            viewModelNotReferenceType);
    }

    /// <summary>
    /// True when <paramref name="viewType"/>'s base-type chain contains
    /// <paramref name="baseType"/> (the platform view base type).
    /// </summary>
    private static bool DerivesFrom(INamedTypeSymbol viewType, INamedTypeSymbol baseType)
    {
        for (var current = viewType.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when any type in <paramref name="viewType"/>'s base-type chain is
    /// an error type (an unresolvable base class).
    /// </summary>
    private static bool BaseChainContainsError(INamedTypeSymbol viewType)
    {
        for (var current = viewType.BaseType; current is not null; current = current.BaseType)
        {
            if (current.TypeKind == TypeKind.Error)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the view or any containing type is file-local: the generated
    /// registration code lives in a different file and cannot reference it.
    /// </summary>
    private static bool IsFileLocal(INamedTypeSymbol viewType)
    {
        for (INamedTypeSymbol? current = viewType; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when any containing type of <paramref name="viewType"/> is generic:
    /// the view's fully qualified name would then contain an unbound type
    /// parameter, which the generated registration code cannot name.
    /// </summary>
    private static bool HasGenericContainingType(INamedTypeSymbol viewType)
    {
        for (var current = viewType.ContainingType; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the generated registration class (a public class in the same
    /// assembly, in an unrelated namespace) can name <paramref name="viewType"/>:
    /// neither the view nor any containing type may be private or protected.
    /// Internal and public are both fine (same assembly); protected internal is
    /// fine through its internal part.
    /// </summary>
    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol viewType)
    {
        for (INamedTypeSymbol? current = viewType; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is
                Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Walks the view's base-type chain for the framework's
    /// <c>BaseView&lt;TViewModel&gt;</c> (WPF or Avalonia). Returns the concrete
    /// <c>TViewModel</c> type argument, or <see langword="null"/> when the view
    /// does not inherit it — or when the argument is an open type parameter,
    /// which cannot be inferred from.
    /// </summary>
    private static INamedTypeSymbol? FindBaseViewModel(INamedTypeSymbol viewType)
    {
        for (var current = viewType.BaseType; current is not null; current = current.BaseType)
        {
            var original = current.OriginalDefinition;
            if (original is { Name: "BaseView", Arity: 1 } &&
                original.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
                    AvaloniaViewsNamespace or WpfViewsNamespace)
            {
                return current.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
            }
        }

        return null;
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

    /// <summary>
    /// True when the mapping already has a diagnostic error (FLOWGEN003-006/008-010).
    /// Such mappings are excluded from the duplicate check to avoid noise diagnostics.
    /// </summary>
    private static bool HasMappingError(ViewMapping mapping) =>
        mapping.CannotInferViewModel ||
        mapping.ConflictingBaseViewModel is not null ||
        mapping.UnresolvableViewModelType ||
        mapping.InvalidLifetime is not null ||
        mapping.IsOpenGenericView ||
        mapping.IsOpenGenericViewModel ||
        mapping.ViewModelNotImplementingInterface is not null ||
        !mapping.ViewConstraintViolations.IsEmpty ||
        mapping.ViewModelNotReferenceType is not null;

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

        // Per-mapping errors are compile-time failures: unresolved view models
        // (FLOWGEN004/006), conflicting explicit/inferred types (FLOWGEN005),
        // and unrecognized lifetimes (FLOWGEN003) would otherwise misbehave
        // silently at runtime. All diagnostics are collected and reported together
        // so the user sees every problem in one compilation, not one at a time.
        bool hasMappingErrors = false;
        foreach (var mapping in mappings)
        {
            if (mapping.CannotInferViewModel)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CannotInferViewModel, mapping.Location, mapping.ViewName));
                hasMappingErrors = true;
            }

            if (mapping.ConflictingBaseViewModel is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConflictingViewModel,
                    mapping.Location,
                    mapping.ViewName,
                    mapping.ViewModelName,
                    mapping.ConflictingBaseViewModel));
                hasMappingErrors = true;
            }

            if (mapping.UnresolvableViewModelType)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnresolvableViewModelType, mapping.Location, mapping.ViewName));
                hasMappingErrors = true;
            }

            if (mapping.InvalidLifetime is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidLifetime, mapping.Location, mapping.InvalidLifetime));
                hasMappingErrors = true;
            }

            if (mapping.IsOpenGenericView)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericView, mapping.Location, mapping.ViewName));
                hasMappingErrors = true;
            }

            if (mapping.IsOpenGenericViewModel)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericViewModel, mapping.Location, mapping.ViewName, mapping.ViewModelName));
                hasMappingErrors = true;
            }

            if (mapping.ViewModelNotImplementingInterface is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ViewModelNotImplementingInterface, mapping.Location, mapping.ViewName, mapping.ViewModelNotImplementingInterface));
                hasMappingErrors = true;
            }

            foreach (var violation in mapping.ViewConstraintViolations)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ViewConstraintViolation, mapping.Location, mapping.ViewName, violation));
                hasMappingErrors = true;
            }

            if (mapping.ViewModelNotReferenceType is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ViewModelNotReferenceType, mapping.Location, mapping.ViewName, mapping.ViewModelNotReferenceType, mapping.RegisterMethod));
                hasMappingErrors = true;
            }
        }

        // Duplicate ViewModel mappings would throw at runtime; fail at compile time instead.
        // Skip mappings that already have errors: their ViewModelName may be empty or
        // otherwise invalid, which would produce noise diagnostics on top of the real ones.
        var seenViewModels = new HashSet<string>();
        foreach (var mapping in mappings)
        {
            if (HasMappingError(mapping))
            {
                continue;
            }

            if (!seenViewModels.Add(mapping.ViewModelName))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateViewModel, mapping.Location, mapping.ViewModelName));
                hasMappingErrors = true;
            }
        }

        if (hasMappingErrors)
        {
            return;
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
        sb.AppendLine("    /// <c>services.AddElegantSeriesFlowViews(...)</c> to register every attributed view.");
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

        public static readonly DiagnosticDescriptor CannotInferViewModel = new DiagnosticDescriptor(
            id: "FLOWGEN004",
            title: "Cannot infer ViewModel type for [ViewFor]",
            messageFormat: "[ViewFor] on view '{0}' does not specify a ViewModel type, and the view does not inherit BaseView<TViewModel>; the ViewModel type cannot be inferred",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ConflictingViewModel = new DiagnosticDescriptor(
            id: "FLOWGEN005",
            title: "Conflicting ViewModel type for [ViewFor]",
            messageFormat: "[ViewFor] on view '{0}' specifies ViewModel '{1}', but the view inherits BaseView<{2}>; the types must match",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnresolvableViewModelType = new DiagnosticDescriptor(
            id: "FLOWGEN006",
            title: "Unresolvable ViewModel type for [ViewFor]",
            messageFormat: "[ViewFor] on view '{0}' specifies a ViewModel type that could not be resolved",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OpenGenericView = new DiagnosticDescriptor(
            id: "FLOWGEN008",
            title: "Open generic view for [ViewFor]",
            messageFormat: "[ViewFor] on view '{0}' is an open generic type; only closed (constructed) view types can be registered",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OpenGenericViewModel = new DiagnosticDescriptor(
            id: "FLOWGEN009",
            title: "Open generic ViewModel for [ViewFor]",
            messageFormat: "[ViewFor] on view '{0}' specifies an open generic ViewModel type '{1}'; only closed (constructed) ViewModel types can be registered",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ViewModelNotImplementingInterface = new DiagnosticDescriptor(
            id: "FLOWGEN010",
            title: "ViewModel does not implement INavigationViewModel",
            messageFormat: "[ViewFor] on view '{0}' specifies ViewModel type '{1}' which does not implement INavigationViewModel",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ViewConstraintViolation = new DiagnosticDescriptor(
            id: "FLOWGEN011",
            title: "View type cannot be registered",
            messageFormat: "[ViewFor] on view '{0}' cannot be registered: {1}",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ViewModelNotReferenceType = new DiagnosticDescriptor(
            id: "FLOWGEN012",
            title: "ViewModel must be a reference type for the selected lifetime",
            messageFormat: "[ViewFor] on view '{0}' registers ViewModel type '{1}' via {2}, which requires a reference-type ViewModel; a value-type ViewModel can only be used with Lifetime ViewOnly",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
