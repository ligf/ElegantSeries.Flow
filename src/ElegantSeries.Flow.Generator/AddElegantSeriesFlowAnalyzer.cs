using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ElegantSeries.Flow.Generator;

/// <summary>
/// Reports a warning when <c>AddElegantSeriesFlow</c> is called with a
/// <c>ServiceLifetime</c> that the navigation service does not support.
/// </summary>
/// <remarks>
/// <para>
/// <c>INavigationService</c> is stateful (it owns the region stacks), so only
/// <c>Singleton</c> and <c>Scoped</c> are valid. <c>Transient</c> (or an undefined
/// value) throws <c>ArgumentOutOfRangeException</c> at runtime; this analyzer
/// surfaces the mistake at compile time instead.
/// </para>
/// <para>
/// Only arguments the compiler can constant-fold are checked (e.g.
/// <c>ServiceLifetime.Transient</c> or <c>(ServiceLifetime)999</c>). A lifetime
/// passed via a variable is still validated by the runtime check.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AddElegantSeriesFlowAnalyzer : DiagnosticAnalyzer
{
    private const string MethodName = "AddElegantSeriesFlow";
    private const string LifetimeParameterName = "lifetime";
    private const string ServiceLifetimeMetadataName = "Microsoft.Extensions.DependencyInjection.ServiceLifetime";

    // Microsoft.Extensions.DependencyInjection.ServiceLifetime:
    // Singleton = 0, Scoped = 1, Transient = 2.
    private const int SingletonValue = 0;
    private const int ScopedValue = 1;

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "FLOWGEN007",
        title: "Unsupported INavigationService lifetime",
        messageFormat: "AddElegantSeriesFlow does not support '{0}'; use ServiceLifetime.Singleton or ServiceLifetime.Scoped",
        category: "ElegantSeries.Flow.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "INavigationService is stateful (it owns the region stacks). Transient lifetime would hand each consumer an isolated, empty navigation stack and silently break navigation; AddElegantSeriesFlow throws ArgumentOutOfRangeException for it at runtime.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method ||
            method.Name != MethodName ||
            method.ContainingType?.Name != "FlowServiceCollectionExtensions" ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != "ElegantSeries.Flow.Extensions")
        {
            return;
        }

        // Find the lifetime argument (positional or named). Omitted means the
        // default (Singleton), which is valid.
        ArgumentSyntax? lifetimeArgument = null;
        for (var i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var argument = invocation.ArgumentList.Arguments[i];
            if (argument.NameColon?.Name.Identifier.Text == LifetimeParameterName)
            {
                lifetimeArgument = argument;
                break;
            }

            if (argument.NameColon is null && i < method.Parameters.Length &&
                method.Parameters[i].Name == LifetimeParameterName)
            {
                lifetimeArgument = argument;
                break;
            }
        }

        if (lifetimeArgument is null)
        {
            return;
        }

        var constant = context.SemanticModel.GetConstantValue(lifetimeArgument.Expression, context.CancellationToken);
        if (!constant.HasValue || constant.Value is not int intValue)
        {
            // Not a compile-time constant (e.g. a variable): the runtime check covers it.
            return;
        }

        if (intValue == SingletonValue || intValue == ScopedValue)
        {
            return;
        }

        var display = GetLifetimeDisplayName(context.Compilation, intValue);
        context.ReportDiagnostic(Diagnostic.Create(Rule, lifetimeArgument.GetLocation(), display));
    }

    private static string GetLifetimeDisplayName(Compilation compilation, int value)
    {
        // Resolve the enum member name semantically so the message shows
        // "ServiceLifetime.Transient" rather than a raw number when possible.
        if (compilation.GetTypeByMetadataName(ServiceLifetimeMetadataName) is INamedTypeSymbol enumType)
        {
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue && member.ConstantValue is int memberValue && memberValue == value)
                {
                    return "ServiceLifetime." + member.Name;
                }
            }
        }

        return "(ServiceLifetime)" + value;
    }
}
