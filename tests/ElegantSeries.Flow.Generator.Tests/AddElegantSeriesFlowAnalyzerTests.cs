using System.Collections.Immutable;
using System.IO;
using System.Linq;
using ElegantSeries.Flow.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ElegantSeries.Flow.Generator.Tests;

/// <summary>
/// Tests for <see cref="AddElegantSeriesFlowAnalyzer"/> (FLOWGEN007).
/// </summary>
/// <remarks>
/// The stub <c>FlowServiceCollectionExtensions</c> is compiled into a separate
/// referenced assembly named <c>ElegantSeries.Flow</c>, mirroring how a real
/// consumer sees the method: through a reference to the Flow assembly. The
/// analyzer verifies that assembly identity, so name-identical types from
/// other assemblies (or defined in the consumer's own source) must not match.
/// </remarks>
public class AddElegantSeriesFlowAnalyzerTests
{
    private const string Stubs = """
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

    private const string ConsumerTransientCall = """
        using ElegantSeries.Flow.Extensions;

        class C
        {
            void M(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
            {
                services.AddElegantSeriesFlow(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient);
            }
        }
        """;

    [Fact]
    public void TransientLifetime_ReportsFlowGen007()
    {
        var diagnostics = RunAnalyzer(ConsumerTransientCall);

        var warning = Assert.Single(diagnostics, d => d.Id == "FLOWGEN007");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("ServiceLifetime.Transient", warning.GetMessage());
    }

    [Fact]
    public void UndefinedLifetimeValue_ReportsFlowGen007()
    {
        var diagnostics = RunAnalyzer("""
            using ElegantSeries.Flow.Extensions;

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
        var diagnostics = RunAnalyzer("""
            using ElegantSeries.Flow.Extensions;

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
        var diagnostics = RunAnalyzer($$"""
            using ElegantSeries.Flow.Extensions;

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
        var diagnostics = RunAnalyzer("""
            using ElegantSeries.Flow.Extensions;

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
        var diagnostics = RunAnalyzer("""
            using ElegantSeries.Flow.Extensions;

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

    [Fact]
    public void SameNamesInDifferentAssembly_NoDiagnostic()
    {
        // A type with the same name in the same namespace, but defined by a
        // different referenced assembly, is not Flow's registration method:
        // the call is not subject to Flow's lifetime restriction and nothing
        // throws at runtime, so the analyzer must stay silent.
        var diagnostics = RunAnalyzer(ConsumerTransientCall, stubAssemblyName: "Contoso.Navigation");

        Assert.Empty(diagnostics.Where(d => d.Id == "FLOWGEN007"));
    }

    [Fact]
    public void SameNamesDefinedInConsumerSource_NoDiagnostic()
    {
        // Same discrimination when the lookalike type is defined in the
        // consumer's own source: its containing assembly is the consumer's,
        // not ElegantSeries.Flow.
        var diagnostics = RunAnalyzerWithInlineStubs(ConsumerTransientCall);

        Assert.Empty(diagnostics.Where(d => d.Id == "FLOWGEN007"));
    }

    private static ImmutableArray<Diagnostic> RunAnalyzer(
        string consumerSource, string stubAssemblyName = "ElegantSeries.Flow")
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(consumerSource) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                BuildStubLibrary(stubAssemblyName),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return RunAnalyzer(compilation);
    }

    private static ImmutableArray<Diagnostic> RunAnalyzerWithInlineStubs(string consumerSource)
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[]
            {
                CSharpSyntaxTree.ParseText(Stubs),
                CSharpSyntaxTree.ParseText(consumerSource),
            },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return RunAnalyzer(compilation);
    }

    private static ImmutableArray<Diagnostic> RunAnalyzer(CSharpCompilation compilation)
    {
        var analyzer = new AddElegantSeriesFlowAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static MetadataReference BuildStubLibrary(string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(Stubs) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        // Emit to an in-memory image so the consumer compilation binds the
        // stub through a real metadata reference, exactly like a built
        // ElegantSeries.Flow.dll (or a renamed lookalike) arriving via NuGet
        // or a project reference — not a compilation reference, whose symbol
        // identity could differ.
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success);
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
