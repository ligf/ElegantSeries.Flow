using System.Collections.Immutable;
using System.Linq;
using ElegantSeries.Flow.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ElegantSeries.Flow.Generator.Tests;

/// <summary>
/// Tests for <see cref="AddElegantSeriesFlowAnalyzer"/> (FLOWGEN007).
/// </summary>
public class AddElegantSeriesFlowAnalyzerTests
{
    private const string Stubs = """
        using ElegantSeries.Flow.Extensions;

        namespace Microsoft.Extensions.DependencyInjection
        {
            public enum ServiceLifetime { Singleton = 0, Scoped = 1, Transient = 2 }
            public interface IServiceCollection { }
        }

        namespace ElegantSeries.Flow.Extensions
        {
            public static class FlowServiceCollectionExtensions
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddElegantSeriesFlow(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime =
                        Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)
                    => services;
            }
        }
        """;

    [Fact]
    public void TransientLifetime_ReportsFlowGen007()
    {
        var diagnostics = RunAnalyzer(Stubs + """
            class C
            {
                void M(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddElegantSeriesFlow(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient);
                }
            }
            """);

        var warning = Assert.Single(diagnostics, d => d.Id == "FLOWGEN007");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("ServiceLifetime.Transient", warning.GetMessage());
    }

    [Fact]
    public void UndefinedLifetimeValue_ReportsFlowGen007()
    {
        var diagnostics = RunAnalyzer(Stubs + """
            class C
            {
                void M(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddElegantSeriesFlow((Microsoft.Extensions.DependencyInjection.ServiceLifetime)999);
                }
            }
            """);

        var warning = Assert.Single(diagnostics, d => d.Id == "FLOWGEN007");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void NamedTransientArgument_ReportsFlowGen007()
    {
        var diagnostics = RunAnalyzer(Stubs + """
            class C
            {
                void M(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddElegantSeriesFlow(lifetime: Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient);
                }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "FLOWGEN007");
    }

    [Theory]
    [InlineData("Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton")]
    [InlineData("Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped")]
    public void SupportedLifetimes_NoDiagnostic(string lifetime)
    {
        var diagnostics = RunAnalyzer(Stubs + $$"""
            class C
            {
                void M(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddElegantSeriesFlow({{lifetime}});
                }
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Id == "FLOWGEN007"));
    }

    [Fact]
    public void OmittedLifetime_NoDiagnostic()
    {
        var diagnostics = RunAnalyzer(Stubs + """
            class C
            {
                void M(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddElegantSeriesFlow();
                }
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Id == "FLOWGEN007"));
    }

    [Fact]
    public void NonConstantLifetime_NoDiagnostic()
    {
        // A lifetime passed via a variable cannot be constant-folded; the
        // runtime ArgumentOutOfRangeException check covers it.
        var diagnostics = RunAnalyzer(Stubs + """
            class C
            {
                void M(Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                       Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime)
                {
                    services.AddElegantSeriesFlow(lifetime);
                }
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Id == "FLOWGEN007"));
    }

    private static ImmutableArray<Diagnostic> RunAnalyzer(string sources)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sources);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new AddElegantSeriesFlowAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
