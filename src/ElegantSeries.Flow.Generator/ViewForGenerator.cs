using System;
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
/// given explicitly (<c>[ViewFor&lt;TViewModel&gt;]</c>) or inferred from the
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
    private const string GenericViewForAttributeMetadataName = "ElegantSeries.Flow.Routing.ViewForAttribute`1";

    // Platform namespace roots. The locator and BaseView<TViewModel> namespaces
    // are derived from these, so supporting a new platform only touches this table.
    private const string AvaloniaNamespaceRoot = "ElegantSeries.Flow.Avalonia";
    private const string WpfNamespaceRoot = "ElegantSeries.Flow.WPF";

    private const string AvaloniaLocatorMetadataName = AvaloniaNamespaceRoot + ".Locating.IViewLocator";
    private const string WpfLocatorMetadataName = WpfNamespaceRoot + ".Locating.IViewLocator";
    private const string GeneratedRegistrationsMarkerMetadataName = "ElegantSeries.Flow.Routing.GeneratedViewRegistrationsAttribute";
    private const string GeneratedNamespaceOverrideMetadataName = "ElegantSeries.Flow.Routing.ViewForGeneratedNamespaceAttribute";

    private const string AvaloniaViewsNamespace = "global::" + AvaloniaNamespaceRoot + ".Views";
    private const string WpfViewsNamespace = "global::" + WpfNamespaceRoot + ".Views";

    private const string LifetimeTransient = "Transient";
    private const string LifetimeSingleton = "Singleton";
    private const string LifetimeViewOnly = "ViewOnly";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Two attribute forms feed the same mapping pipeline: the plain
        // [ViewFor] (ViewModel inferred from the view's BaseView<T>) and the
        // generic [ViewFor<TViewModel>] (ViewModel as the type argument).
        var plainMappings = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ViewForAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => GetMapping(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        var genericMappings = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GenericViewForAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => GetMapping(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        var mappings = plainMappings
            .Combine(genericMappings)
            .Select(static (pair, _) => new ViewMappings(pair.Left.AddRange(pair.Right)));

        // The output step consumes an equatable snapshot of the
        // compilation, never the Compilation itself: a Compilation
        // compares by reference, so feeding it to the output step
        // directly would re-run generation on every compilation
        // change no matter how value-equal everything else is.
        var snapshots = context.CompilationProvider
            .Select(static (compilation, _) => CompilationSnapshot.Create(compilation));

        var inputs = snapshots.Combine(mappings);

        context.RegisterSourceOutput(inputs, static (spc, tuple) =>
            Generate(spc, tuple.Left, tuple.Right));
    }

    private sealed class ViewMapping : IEquatable<ViewMapping>
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

        // Value equality over every field except Location, so the
        // incremental pipeline can actually cache: without it the model
        // compared by reference, every compilation change produced
        // "new" mappings, and the collect/combine/output nodes re-ran
        // in full on every keystroke. Location is the deliberate
        // exception — it only positions this mapping's diagnostics, it
        // never changes the generated output, and Roslyn Location
        // instances have no value equality to compare with. The
        // trade-off: if a mapping's payload is unchanged but its
        // attribute moved within the file, a cached run keeps the
        // diagnostic position from the run that first produced it,
        // until the payload itself changes.
        public bool Equals(ViewMapping? other) =>
            other is not null &&
            string.Equals(ViewName, other.ViewName, StringComparison.Ordinal) &&
            string.Equals(ViewModelName, other.ViewModelName, StringComparison.Ordinal) &&
            string.Equals(RegisterMethod, other.RegisterMethod, StringComparison.Ordinal) &&
            string.Equals(InvalidLifetime, other.InvalidLifetime, StringComparison.Ordinal) &&
            CannotInferViewModel == other.CannotInferViewModel &&
            string.Equals(ConflictingBaseViewModel, other.ConflictingBaseViewModel, StringComparison.Ordinal) &&
            UnresolvableViewModelType == other.UnresolvableViewModelType &&
            IsOpenGenericView == other.IsOpenGenericView &&
            IsOpenGenericViewModel == other.IsOpenGenericViewModel &&
            string.Equals(ViewModelNotImplementingInterface, other.ViewModelNotImplementingInterface, StringComparison.Ordinal) &&
            ViewConstraintViolations.SequenceEqual(other.ViewConstraintViolations) &&
            string.Equals(ViewModelNotReferenceType, other.ViewModelNotReferenceType, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as ViewMapping);

        public override int GetHashCode()
        {
            // Manual combine: the generator targets netstandard2.0,
            // which has no System.HashCode.
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ViewName);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ViewModelName);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(RegisterMethod);
                hash = (hash * 31) + (InvalidLifetime is null ? 0 : StringComparer.Ordinal.GetHashCode(InvalidLifetime));
                hash = (hash * 31) + CannotInferViewModel.GetHashCode();
                hash = (hash * 31) + (ConflictingBaseViewModel is null ? 0 : StringComparer.Ordinal.GetHashCode(ConflictingBaseViewModel));
                hash = (hash * 31) + UnresolvableViewModelType.GetHashCode();
                hash = (hash * 31) + IsOpenGenericView.GetHashCode();
                hash = (hash * 31) + IsOpenGenericViewModel.GetHashCode();
                hash = (hash * 31) + (ViewModelNotImplementingInterface is null ? 0 : StringComparer.Ordinal.GetHashCode(ViewModelNotImplementingInterface));
                foreach (var violation in ViewConstraintViolations)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(violation);
                }

                hash = (hash * 31) + (ViewModelNotReferenceType is null ? 0 : StringComparer.Ordinal.GetHashCode(ViewModelNotReferenceType));
                return hash;
            }
        }
    }

    /// <summary>
    /// The combined mappings of both attribute forms as one
    /// value-compared pipeline value: <see cref="ImmutableArray{T}"/>
    /// itself compares by reference, so the collected array is wrapped
    /// and compared element-wise (each <see cref="ViewMapping"/> is
    /// value-equatable). Without the wrapper the output step's input
    /// would count as changed on every run.
    /// </summary>
    private sealed class ViewMappings : IEquatable<ViewMappings>
    {
        public ViewMappings(ImmutableArray<ViewMapping> items)
        {
            Items = items;
        }

        public ImmutableArray<ViewMapping> Items { get; }

        public bool Equals(ViewMappings? other) =>
            other is not null && Items.SequenceEqual(other.Items);

        public override bool Equals(object? obj) => Equals(obj as ViewMappings);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var item in Items)
                {
                    hash = (hash * 31) + item.GetHashCode();
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// Everything the output step needs from the compilation, projected
    /// into value-compared data: the assembly name (for the generated
    /// namespace), which platform locators are referenced, each present
    /// locator's validated provider registrations, and — when exactly
    /// one platform is referenced — that locator's display name for the
    /// aggregator's extension method. The projection is recomputed
    /// whenever the compilation changes, but as long as the projected
    /// values are unchanged the output step reuses its previous
    /// sources and diagnostics instead of re-running.
    /// </summary>
    private sealed class CompilationSnapshot : IEquatable<CompilationSnapshot>
    {
        private CompilationSnapshot(
            string? assemblyName,
            string? namespaceOverride,
            bool hasAvaloniaLocator,
            bool hasWpfLocator,
            string? singleLocatorDisplayName,
            ImmutableArray<ProviderRegistration> avaloniaProviders,
            ImmutableArray<ProviderRegistration> wpfProviders)
        {
            AssemblyName = assemblyName;
            NamespaceOverride = namespaceOverride;
            HasAvaloniaLocator = hasAvaloniaLocator;
            HasWpfLocator = hasWpfLocator;
            SingleLocatorDisplayName = singleLocatorDisplayName;
            AvaloniaProviders = avaloniaProviders;
            WpfProviders = wpfProviders;
        }

        public string? AssemblyName { get; }

        /// <summary>
        /// The raw value of this assembly's
        /// <c>[assembly: ViewForGeneratedNamespace]</c> attribute, or
        /// <see langword="null"/> when the attribute is absent. An attribute
        /// whose value cannot be read as a string projects as the empty
        /// string (present but invalid).
        /// </summary>
        public string? NamespaceOverride { get; }

        /// <summary>
        /// The namespace this compilation's generated registrations and
        /// aggregator are emitted into: the override when one is given and
        /// valid, otherwise the namespace derived from the assembly name.
        /// <see langword="null"/> exactly when an override is present but
        /// invalid — <c>Generate</c> then reports FLOWGEN016 and emits
        /// nothing.
        /// </summary>
        public string? GeneratedNamespace =>
            NamespaceOverride is null
                ? GetGeneratedNamespace(AssemblyName)
                : IsValidNamespace(NamespaceOverride)
                    ? NamespaceOverride
                    : null;

        public bool HasAvaloniaLocator { get; }

        public bool HasWpfLocator { get; }

        public int PlatformCount =>
            (HasAvaloniaLocator ? 1 : 0) + (HasWpfLocator ? 1 : 0);

        /// <summary>
        /// The referenced locator's fully qualified name without the
        /// <c>global::</c> prefix, when exactly one platform is
        /// referenced; otherwise <see langword="null"/> (the aggregator
        /// is never emitted for zero or two platforms).
        /// </summary>
        public string? SingleLocatorDisplayName { get; }

        /// <summary>
        /// Registration types of provider assemblies validated against
        /// the Avalonia locator; empty when that locator is not
        /// referenced or no provider validates.
        /// </summary>
        public ImmutableArray<ProviderRegistration> AvaloniaProviders { get; }

        /// <summary>
        /// Registration types of provider assemblies validated against
        /// the WPF locator; empty when that locator is not referenced
        /// or no provider validates.
        /// </summary>
        public ImmutableArray<ProviderRegistration> WpfProviders { get; }

        /// <summary>
        /// The providers validated against the single referenced
        /// platform's locator. Only meaningful when
        /// <see cref="PlatformCount"/> is exactly one.
        /// </summary>
        public ImmutableArray<ProviderRegistration> SinglePlatformProviders =>
            HasAvaloniaLocator ? AvaloniaProviders : WpfProviders;

        public static CompilationSnapshot Create(Compilation compilation)
        {
            var avaloniaLocator = compilation.GetTypeByMetadataName(AvaloniaLocatorMetadataName);
            var wpfLocator = compilation.GetTypeByMetadataName(WpfLocatorMetadataName);

            var avaloniaProviders = avaloniaLocator is null
                ? ImmutableArray<ProviderRegistration>.Empty
                : FindReferencedProviders(compilation, avaloniaLocator).ToImmutableArray();
            var wpfProviders = wpfLocator is null
                ? ImmutableArray<ProviderRegistration>.Empty
                : FindReferencedProviders(compilation, wpfLocator).ToImmutableArray();

            string? singleLocatorDisplayName = null;
            if ((avaloniaLocator is null) != (wpfLocator is null))
            {
                singleLocatorDisplayName = (avaloniaLocator ?? wpfLocator)!
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Substring("global::".Length);
            }

            string? namespaceOverride = null;
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    "global::" + GeneratedNamespaceOverrideMetadataName)
                {
                    namespaceOverride =
                        attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is string value
                            ? value
                            : string.Empty;
                    break;
                }
            }

            return new CompilationSnapshot(
                compilation.AssemblyName,
                namespaceOverride,
                avaloniaLocator is not null,
                wpfLocator is not null,
                singleLocatorDisplayName,
                avaloniaProviders,
                wpfProviders);
        }

        public bool Equals(CompilationSnapshot? other) =>
            other is not null &&
            string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal) &&
            string.Equals(NamespaceOverride, other.NamespaceOverride, StringComparison.Ordinal) &&
            HasAvaloniaLocator == other.HasAvaloniaLocator &&
            HasWpfLocator == other.HasWpfLocator &&
            string.Equals(SingleLocatorDisplayName, other.SingleLocatorDisplayName, StringComparison.Ordinal) &&
            AvaloniaProviders.SequenceEqual(other.AvaloniaProviders) &&
            WpfProviders.SequenceEqual(other.WpfProviders);

        public override bool Equals(object? obj) => Equals(obj as CompilationSnapshot);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (AssemblyName is null ? 0 : StringComparer.Ordinal.GetHashCode(AssemblyName));
                hash = (hash * 31) + (NamespaceOverride is null ? 0 : StringComparer.Ordinal.GetHashCode(NamespaceOverride));
                hash = (hash * 31) + HasAvaloniaLocator.GetHashCode();
                hash = (hash * 31) + HasWpfLocator.GetHashCode();
                hash = (hash * 31) + (SingleLocatorDisplayName is null ? 0 : StringComparer.Ordinal.GetHashCode(SingleLocatorDisplayName));
                foreach (var provider in AvaloniaProviders)
                {
                    hash = (hash * 31) + provider.GetHashCode();
                }

                foreach (var provider in WpfProviders)
                {
                    hash = (hash * 31) + provider.GetHashCode();
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// One referenced provider assembly's validated registrations type, as
    /// projected into <see cref="CompilationSnapshot"/>: the raw assembly
    /// name (the aggregation ordering key and the name FLOWGEN015 reports),
    /// the registrations type's full name (recorded in the provider's
    /// marker, or derived from the assembly name for markers that carry
    /// none), and the reference alias the aggregator qualifies its call
    /// with — <see langword="null"/> when the type is reachable through
    /// <c>global::</c>.
    /// </summary>
    private sealed class ProviderRegistration : IEquatable<ProviderRegistration>
    {
        public ProviderRegistration(string assemblyName, string typeName, string? alias)
        {
            AssemblyName = assemblyName;
            TypeName = typeName;
            Alias = alias;
        }

        public string AssemblyName { get; }

        public string TypeName { get; }

        public string? Alias { get; }

        public bool Equals(ProviderRegistration? other) =>
            other is not null &&
            string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal) &&
            string.Equals(TypeName, other.TypeName, StringComparison.Ordinal) &&
            string.Equals(Alias, other.Alias, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as ProviderRegistration);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(AssemblyName);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypeName);
                hash = (hash * 31) + (Alias is null ? 0 : StringComparer.Ordinal.GetHashCode(Alias));
                return hash;
            }
        }
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

        // The ViewModel type is either explicit ([ViewFor<VM>]) or inferred
        // from the view's BaseView<TViewModel> base class ([ViewFor]).
        var baseViewModel = FindBaseViewModel(viewType);
        INamedTypeSymbol? explicitViewModelSymbol = null;
        string? viewModelName = null;
        var cannotInfer = false;
        string? conflictingBaseViewModel = null;
        var unresolvableViewModelType = false;
        var isOpenGenericViewModel = false;
        string? viewModelNotImplementingInterface = null;

        // The explicit ViewModel type exists only on the generic attribute
        // form, as its type argument: [ViewFor<VM>]. The plain [ViewFor]
        // carries no type at all; its ViewModel is inferred from the view's
        // BaseView<TViewModel> base class below.
        INamedTypeSymbol? explicitViewModel = null;
        var explicitUnresolvable = false;
        if (attribute.AttributeClass is { IsGenericType: true } genericAttributeClass)
        {
            if (genericAttributeClass.TypeArguments[0] is INamedTypeSymbol genericViewModel)
            {
                // Error named types land here too: an unresolved type argument
                // is an INamedTypeSymbol with TypeKind Error (IErrorTypeSymbol
                // derives from INamedTypeSymbol), and the explicit path below
                // handles it (the compiler already reports the bad name).
                explicitViewModel = genericViewModel;
            }
            else
            {
                // Anything that is not a named type (a type parameter, an
                // array type, ...) cannot be resolved to a registrable
                // ViewModel here: FLOWGEN006.
                explicitUnresolvable = true;
            }
        }

        if (explicitViewModel is not null)
        {
            explicitViewModelSymbol = explicitViewModel;
            viewModelName = explicitViewModel.ToDisplayString(format);
            // Open generic ViewModels (e.g. DetailViewModel<T> over a type
            // parameter) cannot be registered: the generated code would
            // reference an undefined type parameter. (An unbound generic
            // cannot be written as a type argument from source; that arm of
            // the check only serves symbols constructed outside source.)
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
        else if (explicitUnresolvable)
        {
            // The type argument could not be resolved to a named type (e.g.
            // it is a type parameter of a generic view): surface FLOWGEN006
            // instead of silently skipping the view.
            unresolvableViewModelType = true;
        }
        else if (baseViewModel is not null)
        {
            viewModelName = baseViewModel.ToDisplayString(format);
        }
        else
        {
            cannotInfer = true;
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
        // A record view is in the same position: it is rejected below
        // with a FLOWGEN011 violation that names the real problem, so
        // the inference failure is suppressed for it too.
        if (isOpenGenericView || viewType.IsRecord)
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

        // A record class can never be a view: a record's base type can only
        // be object or another record, so it cannot derive from the platform
        // view base type the locator requires. Report that single fact and
        // skip the checks below — derivation and constructor questions are
        // derivative for a record, and their answers would only stack
        // secondary violations on top of it. (A record struct cannot carry
        // [ViewFor] at all: the attribute targets classes, so the compiler
        // rejects that form itself.) Skipped for open generic records:
        // FLOWGEN008 already rejects them, and the record violation would
        // only stack on top of it — the same suppression the checks below
        // observe for open generic views.
        if (viewType.IsRecord && !isOpenGenericView)
        {
            viewConstraintViolations.Add("it is a record class; a record cannot derive from the platform view base type, so it cannot be registered as a view");
        }

        if (!isOpenGenericView && !viewType.IsRecord)
        {
            var compilation = ctx.SemanticModel.Compilation;

            // The platform is identified by which Flow locator the compilation
            // references (mirrors Generate); the base type follows from it.
            // When neither locator is referenced, Generate reports FLOWGEN014
            // and suppresses per-mapping reports, so skipping the derivation
            // check loses no diagnostic; when both are referenced, FLOWGEN002
            // does the same.
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
    /// True when the mapping already has a diagnostic error (FLOWGEN003-006/008-012).
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

    /// <summary>
    /// Computes the default namespace the registrations are generated into
    /// for one compilation: the sanitized assembly name as a sub-namespace
    /// of <c>ElegantSeries.Flow.Generated</c>, the layer that holds the
    /// library's generated code — the registrations stay out of the
    /// consuming assembly's own namespace tree, and the layer is reserved
    /// for generated code: no library type lives in it, and the root
    /// namespace never gains a type named <c>Generated</c> (a guard test
    /// pins that reservation, because a library type at either spot would
    /// collide with this namespace in consuming compilations). The
    /// derivation is the default only — a
    /// <c>[assembly: ViewForGeneratedNamespace]</c> attribute replaces it
    /// in full (see <see cref="CompilationSnapshot.GeneratedNamespace"/>),
    /// and because sanitization is many-to-one, two different assembly
    /// names can still derive the same namespace; that collision is the
    /// consuming compilation's to resolve (FLOWGEN015). A missing assembly
    /// name falls back to a fixed <c>Unknown</c> segment under the same
    /// layer.
    /// </summary>
    private static string GetGeneratedNamespace(string? assemblyName)
    {
        var root = SanitizeNamespaceRoot(assemblyName);
        return "ElegantSeries.Flow.Generated." + (root ?? "Unknown");
    }

    /// <summary>
    /// Turns an assembly name into a namespace root (the generated
    /// namespace is <c>ElegantSeries.Flow.Generated</c> plus this root as
    /// its sub-namespace): dots are kept as segment separators, every
    /// other invalid character becomes '_', a segment that would start
    /// with a digit is prefixed with '_', and a segment equal to a
    /// reserved keyword gets a trailing '_'. The mapping is many-to-one —
    /// names that differ only in invalid characters (such as
    /// <c>Contoso-UI</c> and <c>Contoso_UI</c>) produce the same root — so
    /// it does not by itself guarantee distinct registrations types across
    /// assemblies; see <see cref="GetGeneratedNamespace"/>. Returns null
    /// for a missing assembly name (the caller then falls back to a fixed
    /// placeholder segment).
    /// </summary>
    private static string? SanitizeNamespaceRoot(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        var segments = assemblyName!.Split('.');
        var sb = new StringBuilder();
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('.');
            }

            var start = sb.Length;
            foreach (var c in segments[i])
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            if (sb.Length == start)
            {
                sb.Append('_');
            }
            else if (char.IsDigit(sb[start]))
            {
                sb.Insert(start, '_');
            }

            if (ReservedKeywords.Contains(sb.ToString(start, sb.Length - start)))
            {
                sb.Append('_');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// True when <paramref name="value"/> can be emitted verbatim as a
    /// namespace: one or more dot-separated identifiers under
    /// <see cref="IsValidIdentifier"/>. This is the validity rule for a
    /// <c>[assembly: ViewForGeneratedNamespace]</c> value (FLOWGEN016) —
    /// the strict counterpart of <see cref="SanitizeNamespaceRoot"/>,
    /// which repairs an assembly name instead of judging a chosen one.
    /// </summary>
    private static bool IsValidNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var segment in value.Split('.'))
        {
            if (!IsValidIdentifier(segment))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="value"/> is an identifier the generator
    /// can emit unescaped: it starts with a letter or '_', continues with
    /// letters, digits or '_', and is not a reserved keyword. Used for
    /// namespace segments and for reference aliases (an
    /// <c>extern alias</c> name obeys the same rule).
    /// </summary>
    private static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!(char.IsLetterOrDigit(value[i]) || value[i] == '_'))
            {
                return false;
            }
        }

        return !ReservedKeywords.Contains(value);
    }

    private static readonly HashSet<string> ReservedKeywords = new HashSet<string>
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    };

    private static void Generate(
        SourceProductionContext spc,
        CompilationSnapshot snapshot,
        ViewMappings mappings)
    {
        if (snapshot.GeneratedNamespace is null &&
            (!mappings.Items.IsEmpty ||
             (snapshot.PlatformCount == 1 && !snapshot.SinglePlatformProviders.IsEmpty)))
        {
            // An invalid [assembly: ViewForGeneratedNamespace] value leaves
            // no namespace to generate into; like this compilation's own
            // mapping errors, it cancels all generation for the assembly.
            // The report is gated on the compilation actually generating —
            // attributed views of its own, or an aggregation it would emit:
            // a compilation with neither stays silent, the same gating the
            // ambiguous-platform report follows, so a stray attribute in a
            // shared assembly-info file cannot break inert projects.
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidGeneratedNamespace, Location.None, snapshot.NamespaceOverride));
            return;
        }

        if (mappings.Items.IsEmpty)
        {
            // No attributed views of our own: the only possible output is the
            // aggregation over referenced provider assemblies.
            EmitAggregatorForProviderOnlyCompilation(spc, snapshot);
            return;
        }

        var platformCount = snapshot.PlatformCount;
        if (platformCount == 0)
        {
            // Mappings exist (the empty case took the provider-only path),
            // but with no platform locator referenced no registration can
            // ever be generated for this compilation — a dead declaration
            // that would otherwise surface only as a runtime "view not
            // registered". One error naming the root cause, mirroring the
            // ambiguous-platform path; the per-mapping reports below stay
            // suppressed next to it.
            spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NoPlatformReferenced, Location.None));
            return;
        }

        if (platformCount > 1)
        {
            spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.AmbiguousPlatform, Location.None));
            return;
        }

        var locatorName = snapshot.HasAvaloniaLocator ? AvaloniaLocatorMetadataName : WpfLocatorMetadataName;

        // Per-mapping errors are compile-time failures: unresolved view models
        // (FLOWGEN004/006), conflicting explicit/inferred types (FLOWGEN005),
        // and unrecognized lifetimes (FLOWGEN003) would otherwise misbehave
        // silently at runtime. All diagnostics are collected and reported together
        // so the user sees every problem in one compilation, not one at a time.
        bool hasMappingErrors = false;
        foreach (var mapping in mappings.Items)
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

        // One view, one [ViewFor] declaration: the plain and generic attribute
        // classes are separate types, so a single view can carry both. They are
        // alternatives, never cumulative - not even when they agree - because
        // the mapping must be declared exactly once. Report each extra
        // declaration and keep it out of the duplicate-ViewModel check below,
        // whose message would blame other views instead.
        var seenViews = new HashSet<string>();
        var duplicateViewIndexes = new HashSet<int>();
        for (var i = 0; i < mappings.Items.Length; i++)
        {
            var mapping = mappings.Items[i];
            if (HasMappingError(mapping))
            {
                continue;
            }

            if (!seenViews.Add(mapping.ViewName))
            {
                // Tracked by index, not by mapping: ViewMapping compares
                // by value, so a set of mappings could not tell this
                // extra declaration apart from the view's first one when
                // the two agree — both would be excluded below, and a
                // third view claiming the same ViewModel would then
                // escape the duplicate-ViewModel check.
                duplicateViewIndexes.Add(i);
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateViewDeclaration, mapping.Location, mapping.ViewName));
                hasMappingErrors = true;
            }
        }

        // Duplicate ViewModel mappings would throw at runtime; fail at compile time instead.
        // Skip mappings that already have errors: their ViewModelName may be empty or
        // otherwise invalid, which would produce noise diagnostics on top of the real ones.
        var seenViewModels = new HashSet<string>();
        for (var i = 0; i < mappings.Items.Length; i++)
        {
            var mapping = mappings.Items[i];
            if (HasMappingError(mapping) || duplicateViewIndexes.Contains(i))
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

        var ordered = mappings.Items
            .OrderBy(m => m.ViewModelName, System.StringComparer.Ordinal)
            .ThenBy(m => m.ViewName, System.StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file was generated by the ElegantSeries.Flow source generator from");
        sb.AppendLine("// [ViewFor] attributes. Do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        var generatedNamespace = snapshot.GeneratedNamespace!;
        sb.AppendLine("[assembly: global::ElegantSeries.Flow.Routing.GeneratedViewRegistrations(\"" + generatedNamespace + ".ViewForRegistrations\")]");
        sb.AppendLine();
        sb.AppendLine("namespace " + generatedNamespace);
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

        EmitAggregator(spc, snapshot, ownEmitted: true);
    }

    /// <summary>
    /// The provider-only path of <see cref="Generate"/>: this compilation has
    /// no attributed views of its own, so the aggregation over referenced
    /// provider assemblies is the only possible output. Platform ambiguity is
    /// reported as on the attributed-views path, but only when a provider
    /// validated against at least one of the candidate locators is actually
    /// referenced; a compilation with neither views nor validated providers
    /// stays silent.
    /// </summary>
    private static void EmitAggregatorForProviderOnlyCompilation(
        SourceProductionContext spc,
        CompilationSnapshot snapshot)
    {
        if (snapshot.PlatformCount == 0)
        {
            return;
        }

        if (snapshot.PlatformCount > 1)
        {
            if (!snapshot.AvaloniaProviders.IsEmpty || !snapshot.WpfProviders.IsEmpty)
            {
                spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.AmbiguousPlatform, Location.None));
            }

            return;
        }

        EmitAggregator(spc, snapshot, ownEmitted: false);
    }

    /// <summary>
    /// Emits <c>ViewForRegistrationsAggregator.RegisterAllAttributedViews</c>
    /// into this compilation's generated namespace when at least one
    /// referenced assembly is a validated registration provider. The
    /// aggregator calls this assembly's own registrations first (when they
    /// were emitted), then each provider's own
    /// <c>RegisterAttributedViews</c> — never a provider's aggregator, so
    /// every assembly's views are registered exactly once per call. A call
    /// is qualified by the provider reference's alias when the type is not
    /// reachable through <c>global::</c>. When two registrations types
    /// share a full name under the same qualifier (this assembly's own
    /// included) the calls cannot be told apart: the group is reported as
    /// FLOWGEN015 and no aggregator is emitted.
    /// </summary>
    private static void EmitAggregator(
        SourceProductionContext spc,
        CompilationSnapshot snapshot,
        bool ownEmitted)
    {
        var providers = snapshot.SinglePlatformProviders;
        if (providers.IsEmpty)
        {
            return;
        }

        var generatedNamespace = snapshot.GeneratedNamespace!;
        var locatorName = snapshot.SingleLocatorDisplayName!;

        var calls = new List<(string AssemblyName, string TypeName, string? Alias)>();
        if (ownEmitted)
        {
            calls.Add((snapshot.AssemblyName ?? "this assembly", generatedNamespace + ".ViewForRegistrations", null));
        }

        foreach (var provider in providers)
        {
            calls.Add((provider.AssemblyName, provider.TypeName, provider.Alias));
        }

        var hasCollision = false;
        foreach (var group in calls.GroupBy(call => (call.TypeName, call.Alias)))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            hasCollision = true;
            var assemblies = string.Join(", ", group.Select(call => "'" + call.AssemblyName + "'"));
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RegistrationNamespaceCollision, Location.None, assemblies, group.Key.TypeName));
        }

        if (hasCollision)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file was generated by the ElegantSeries.Flow source generator.");
        sb.AppendLine("// Do not edit manually.");
        var aliases = providers
            .Select(provider => provider.Alias)
            .Where(alias => alias is not null)
            .Select(alias => alias!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(alias => alias, StringComparer.Ordinal)
            .ToList();
        foreach (var alias in aliases)
        {
            sb.AppendLine("extern alias " + alias + ";");
        }

        if (aliases.Count > 0)
        {
            sb.AppendLine();
        }

        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace " + generatedNamespace);
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Aggregated view registrations generated from <c>[ViewFor]</c> attributes:");
        sb.AppendLine("    /// this assembly's own registrations (when it has any) plus the");
        sb.AppendLine("    /// registrations of every referenced assembly that carries generated");
        sb.AppendLine("    /// view registrations.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ViewForRegistrationsAggregator");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Registers this assembly's attributed views and the attributed views");
        sb.AppendLine("        /// of every referenced view-registration provider assembly.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"views\">The view locator to register on.</param>");
        sb.Append("        public static void RegisterAllAttributedViews(this global::");
        sb.Append(locatorName);
        sb.AppendLine(" views)");
        sb.AppendLine("        {");
        if (ownEmitted)
        {
            sb.Append("            global::");
            sb.Append(generatedNamespace);
            sb.AppendLine(".ViewForRegistrations.RegisterAttributedViews(views);");
        }

        foreach (var provider in providers)
        {
            sb.Append("            ");
            sb.Append(provider.Alias is null ? "global::" : provider.Alias + "::");
            sb.Append(provider.TypeName);
            sb.AppendLine(".RegisterAttributedViews(views);");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        spc.AddSource("ViewForRegistrationsAggregator.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Finds the registration types of referenced provider assemblies,
    /// sorted by raw assembly name. Recognition is deliberately structural
    /// and self-referential — every signal is an artifact this generator
    /// itself emits, and no user code is matched by name: the assembly must
    /// carry the generated-registrations marker, a type must exist at the
    /// location the marker records (or, for markers emitted by older
    /// generator versions that record none, the location derived from the
    /// assembly name), and that type must offer the exact registration
    /// method shape against this compilation's platform locator: a public
    /// type, and on it a public, static, non-generic method taking exactly
    /// that locator, so the qualified static call the aggregator emits can
    /// actually bind. The recorded location must also be emittable as a
    /// qualified name (every segment a non-keyword identifier). A marked
    /// assembly that fails a later check (a hand-forged marker, or output
    /// of an incompatible generator version) is silently skipped. Each result also carries the alias of the
    /// reference that brought the assembly in, which the aggregator
    /// qualifies its call with; assemblies are no longer folded together
    /// when their type names coincide — that is a collision for
    /// <see cref="EmitAggregator"/> to report, not a duplicate to hide.
    /// </summary>
    private static List<ProviderRegistration> FindReferencedProviders(Compilation compilation, INamedTypeSymbol locator)
    {
        var result = new List<ProviderRegistration>();
        var seen = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly ||
                SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) ||
                !seen.Add(assembly))
            {
                continue;
            }

            var marked = false;
            string? recordedTypeName = null;
            foreach (var attribute in assembly.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    "global::" + GeneratedRegistrationsMarkerMetadataName)
                {
                    marked = true;
                    if (attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is string recorded &&
                        recorded.Length > 0)
                    {
                        recordedTypeName = recorded;
                    }

                    break;
                }
            }

            if (!marked)
            {
                continue;
            }

            var typeName = recordedTypeName ?? GetGeneratedNamespace(assembly.Name) + ".ViewForRegistrations";
            if (!IsEmittableTypeName(typeName))
            {
                continue;
            }

            var type = FindTypeByFullName(assembly.GlobalNamespace, typeName);
            if (type is null || type.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            // The emitted aggregator calls the method through a fully
            // qualified static call, so validation must establish that such
            // a call can actually bind: the method must be public, static,
            // non-generic, and take exactly this compilation's platform
            // locator. Anything else (a forged marker, output of an
            // incompatible generator version) is skipped.
            var locatorFullName = locator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var hasMethod = type.GetMembers("RegisterAttributedViews")
                .OfType<IMethodSymbol>()
                .Any(m => m.IsStatic
                    && !m.IsGenericMethod
                    && m.DeclaredAccessibility == Accessibility.Public
                    && m.Parameters.Length == 1
                    && m.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == locatorFullName);
            if (!hasMethod)
            {
                continue;
            }

            if (!TryChooseCallAlias(reference, out var alias))
            {
                continue;
            }

            result.Add(new ProviderRegistration(assembly.Name, typeName, alias));
        }

        // Sorted by the raw assembly name, ordinal — the ordering the
        // aggregation contract promises; the sanitized type name would order
        // some name pairs differently.
        return result
            .OrderBy(p => p.AssemblyName, System.StringComparer.Ordinal)
            .ThenBy(p => p.TypeName, System.StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Picks the qualifier the aggregator calls a provider through, from
    /// the aliases of the reference that brought its assembly in:
    /// <paramref name="alias"/> is <see langword="null"/> (a
    /// <c>global::</c> call) when the alias list is empty or contains
    /// <c>global</c>, otherwise the ordinally smallest usable alias name.
    /// Returns <see langword="false"/> when the list offers neither — such
    /// a provider cannot be called at all and is skipped like any other
    /// unbindable provider. Only the binding reference is consulted: when
    /// the same assembly is referenced more than once, the compilation
    /// itself binds the first reference and yields no symbol for the rest,
    /// so the qualifier follows the compilation's own binding.
    /// </summary>
    private static bool TryChooseCallAlias(MetadataReference reference, out string? alias)
    {
        string? best = null;
        foreach (var candidate in reference.Properties.Aliases)
        {
            if (candidate == "global")
            {
                alias = null;
                return true;
            }

            if (IsValidIdentifier(candidate) &&
                (best is null || StringComparer.Ordinal.Compare(candidate, best) < 0))
            {
                best = candidate;
            }
        }

        alias = best;
        return best is not null || reference.Properties.Aliases.IsEmpty;
    }

    /// <summary>
    /// True when every segment of a dotted type name is an identifier the
    /// aggregator can emit verbatim into a qualified call. A recorded
    /// location whose namespace contains a keyword segment (a hand-forged
    /// marker can point at a type declared inside
    /// <c>namespace My.@event</c>) resolves through metadata names but
    /// cannot be written into C# source — such a provider fails
    /// recognition like any other unbindable shape.
    /// </summary>
    private static bool IsEmittableTypeName(string typeName)
    {
        foreach (var segment in typeName.Split('.'))
        {
            if (!IsValidIdentifier(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static INamedTypeSymbol? FindTypeByFullName(INamespaceSymbol root, string fullMetadataName)
    {
        var parts = fullMetadataName.Split('.');
        var current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var next = current.GetNamespaceMembers().FirstOrDefault(n => n.Name == parts[i]);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current.GetTypeMembers(parts[parts.Length - 1]).FirstOrDefault();
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

        public static readonly DiagnosticDescriptor DuplicateViewDeclaration = new DiagnosticDescriptor(
            id: "FLOWGEN013",
            title: "View carries more than one [ViewFor] attribute",
            messageFormat: "View '{0}' carries more than one [ViewFor] attribute. Declare the ViewModel mapping exactly once: [ViewFor] and [ViewFor<TViewModel>] are alternatives, not cumulative.",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoPlatformReferenced = new DiagnosticDescriptor(
            id: "FLOWGEN014",
            title: "No view locator platform referenced",
            messageFormat: "This compilation declares [ViewFor] view mappings, but neither the WPF nor the Avalonia view locator is referenced, so no view registrations can be generated for it. Reference ElegantSeries.Flow.WPF or ElegantSeries.Flow.Avalonia in this project.",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RegistrationNamespaceCollision = new DiagnosticDescriptor(
            id: "FLOWGEN015",
            title: "Generated view registration namespace collision",
            messageFormat: "Assemblies {0} resolve to the same generated view registrations type '{1}', so the aggregator cannot tell them apart and no aggregation was generated. Specify a different generated namespace for one of them with [assembly: ViewForGeneratedNamespace(\"...\")], or reference one of them under a distinct alias (the Aliases metadata on the reference) so its registrations can be called through that alias.",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidGeneratedNamespace = new DiagnosticDescriptor(
            id: "FLOWGEN016",
            title: "Invalid [ViewForGeneratedNamespace] namespace",
            messageFormat: "'{0}' is not a valid namespace for [assembly: ViewForGeneratedNamespace]. Use one or more dot-separated identifiers, each starting with a letter or '_' and containing only letters, digits and '_', and none equal to a C# keyword.",
            category: "ElegantSeries.Flow.Generator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
