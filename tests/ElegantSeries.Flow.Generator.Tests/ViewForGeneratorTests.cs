using System.Collections.Immutable;
using System.Linq;
using ElegantSeries.Flow.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ElegantSeries.Flow.Generator.Tests;

/// <summary>
/// Tests for <see cref="ViewForGenerator"/>.
/// </summary>
public class ViewForGeneratorTests
{
    private const string AttributeStub = """
        namespace ElegantSeries.Flow.Routing
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class ViewForAttribute : System.Attribute
            {
                public ViewForAttribute() { }
                public ViewForAttribute(System.Type viewModelType) { ViewModelType = viewModelType; }
                public System.Type? ViewModelType { get; }
                public ViewModelLifetime Lifetime { get; set; } = ViewModelLifetime.Transient;
            }

            public enum ViewModelLifetime { Transient, Singleton, ViewOnly }
        }
        """;

    private const string AvaloniaBaseViewStub = """
        namespace ElegantSeries.Flow.Avalonia.Views
        {
            public abstract class BaseView<TViewModel>
            {
            }
        }
        """;

    private const string AvaloniaLocatorStub = """
        namespace ElegantSeries.Flow.Avalonia.Locating
        {
            public interface IViewLocator
            {
                void Register<TView, TViewModel>();
                void RegisterTransient<TView, TViewModel>();
                void RegisterSingleton<TView, TViewModel>();
            }
        }
        """;

    private const string WpfLocatorStub = """
        namespace ElegantSeries.Flow.WPF.Locating
        {
            public interface IViewLocator
            {
                void Register<TView, TViewModel>();
                void RegisterTransient<TView, TViewModel>();
                void RegisterSingleton<TView, TViewModel>();
            }
        }
        """;

    private const string BasicViews = """
        namespace TestApp.ViewModels
        {
            public class HomeViewModel { }
            public class MenuViewModel { }
            public class DetailViewModel { }
        }

        namespace TestApp.Views
        {
            [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
            public class HomeView { }

            [ElegantSeries.Flow.Routing.ViewFor(
                typeof(TestApp.ViewModels.MenuViewModel),
                Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.Singleton)]
            public class MenuView { }

            [ElegantSeries.Flow.Routing.ViewFor(
                typeof(TestApp.ViewModels.DetailViewModel),
                Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.ViewOnly)]
            public class DetailView { }
        }
        """;

    [Fact]
    public void EmitsRegistrationsForAttributedViews()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + BasicViews;
        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains(
            "this global::ElegantSeries.Flow.Avalonia.Locating.IViewLocator views", text);
        Assert.Contains(
            "views.RegisterTransient<global::TestApp.Views.HomeView, global::TestApp.ViewModels.HomeViewModel>();",
            text);
        Assert.Contains(
            "views.RegisterSingleton<global::TestApp.Views.MenuView, global::TestApp.ViewModels.MenuViewModel>();",
            text);
        Assert.Contains(
            "views.Register<global::TestApp.Views.DetailView, global::TestApp.ViewModels.DetailViewModel>();",
            text);

        // The generated code must compile against the stub locator.
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void DuplicateViewModel_ReportsFlowGen001()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
                public class HomeView { }

                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
                public class OtherHomeView { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN001");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    [Fact]
    public void MultipleErrors_AllReportedTogether()
    {
        // Two views with unresolvable problems: both diagnostics must be reported
        // in a single compilation, not one at a time.
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView { }

                [ElegantSeries.Flow.Routing.ViewFor]
                public class OtherView { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        Assert.Equal(2, diagnostics.Count(d => d.Id == "FLOWGEN004"));
    }

    [Fact]
    public void BothPlatformsReferenced_ReportsFlowGen002()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + WpfLocatorStub + BasicViews;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN002");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void ValidLifetimeCast_EmitsCorrectRegistration()
    {
        // Guards the fix for a second silent bug in the old syntax-based parsing:
        // a *valid* integral cast such as (ViewModelLifetime)1 was downgraded to
        // Transient because only MemberAccessExpressionSyntax was recognized.
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class MenuViewModel { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(
                    typeof(TestApp.ViewModels.MenuViewModel),
                    Lifetime = (ElegantSeries.Flow.Routing.ViewModelLifetime)1)]
                public class MenuView { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.RegisterSingleton<global::TestApp.Views.MenuView, global::TestApp.ViewModels.MenuViewModel>();",
            generated.SourceText.ToString());
    }

    [Fact]
    public void InvalidLifetimeCast_ReportsFlowGen003()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(
                    typeof(TestApp.ViewModels.HomeViewModel),
                    Lifetime = (ElegantSeries.Flow.Routing.ViewModelLifetime)99)]
                public class HomeView { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN003");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void NoPlatformLocator_EmitsNothing()
    {
        var sources = AttributeStub + BasicViews;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InfersViewModelFromBaseView()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel>
                {
                }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.RegisterTransient<global::TestApp.Views.HomeView, global::TestApp.ViewModels.HomeViewModel>();",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void InfersViewModelFromIndirectBaseView()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel { }
            }

            namespace TestApp.Views
            {
                public class MiddleView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel>
                {
                }

                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView : MiddleView
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.RegisterTransient<global::TestApp.Views.HomeView, global::TestApp.ViewModels.HomeViewModel>();",
            generated.SourceText.ToString());
    }

    [Fact]
    public void OpenGenericView_ReportsFlowGen008()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
                public class GenericView<T>
                {
                }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN008");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void NullViewModelType_ReportsFlowGen006()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(null)]
                public class HomeView
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN006");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void MissingBaseView_ReportsFlowGen004()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN004");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void ConflictingViewModel_ReportsFlowGen005()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel { }
                public class OtherViewModel { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.OtherViewModel))]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel>
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN005");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void MatchingExplicitViewModel_NoDiagnostic()
    {
        var sources = AttributeStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel>
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.RegisterTransient<global::TestApp.Views.HomeView, global::TestApp.ViewModels.HomeViewModel>();",
            generated.SourceText.ToString());
    }

    private static (GeneratorRunResult RunResult, ImmutableArray<Diagnostic> Diagnostics, Compilation UpdatedCompilation)
        RunGenerator(string sources)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sources);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ViewForGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        var runResult = driver.GetRunResult();

        return (runResult.Results.Single(), runResult.Diagnostics, updatedCompilation);
    }

    [Fact]
    public void OpenGenericViewModel_ReportsFlowGen009()
    {
        // [ViewFor(typeof(DetailViewModel<>))] with an open generic ViewModel must
        // produce FLOWGEN009, not generate uncompilable registration code.
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.DetailViewModel<>))]
                public class DetailView
                {
                }
            }

            namespace TestApp.ViewModels
            {
                public class DetailViewModel<T>
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN009");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void ClosedGenericViewModel_DoesNotReportFlowGen009()
    {
        // A closed generic ViewModel (e.g. DetailViewModel<string>) is fine.
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.DetailViewModel<string>))]
                public class DetailView
                {
                }
            }

            namespace TestApp.ViewModels
            {
                public class DetailViewModel<T>
                {
                }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Id == "FLOWGEN009"));
    }
}
