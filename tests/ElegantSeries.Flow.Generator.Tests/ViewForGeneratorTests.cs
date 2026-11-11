using System.Collections.Generic;
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
                public ViewModelLifetime Lifetime { get; set; } = ViewModelLifetime.Transient;
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class ViewForAttribute<TViewModel> : System.Attribute
            {
                public ViewModelLifetime Lifetime { get; set; } = ViewModelLifetime.Transient;
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
            public sealed class GeneratedViewRegistrationsAttribute : System.Attribute
            {
                public GeneratedViewRegistrationsAttribute() { }
                public GeneratedViewRegistrationsAttribute(string registrationsTypeFullName) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
            public sealed class ViewForGeneratedNamespaceAttribute : System.Attribute
            {
                public ViewForGeneratedNamespaceAttribute(string @namespace) { Namespace = @namespace; }
                public string Namespace { get; }
            }

            public enum ViewModelLifetime { Transient, Singleton, ViewOnly }
        }
        """;

    private const string AvaloniaControlsStub = """
        namespace Avalonia.Controls
        {
            public class Control { }
            public class ContentControl : Control { }
            public class UserControl : ContentControl { }
        }
        """;

    private const string AvaloniaBaseViewStub = """
        namespace ElegantSeries.Flow.Avalonia.Views
        {
            public abstract class BaseView<TViewModel> : global::Avalonia.Controls.UserControl
                where TViewModel : class, ElegantSeries.Flow.Navigation.INavigationViewModel
            {
            }
        }
        """;

    // Stub constraints mirror the production IViewLocator / BaseView exactly:
    // TView : Control, new() on all three methods; TViewModel : INavigationViewModel
    // on Register and class, INavigationViewModel on RegisterTransient/RegisterSingleton.
    private const string AvaloniaLocatorStub = """
        namespace ElegantSeries.Flow.Avalonia.Locating
        {
            public interface IViewLocator
            {
                void Register<TView, TViewModel>()
                    where TView : global::Avalonia.Controls.Control, new()
                    where TViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel;
                void RegisterTransient<TView, TViewModel>()
                    where TView : global::Avalonia.Controls.Control, new()
                    where TViewModel : class, ElegantSeries.Flow.Navigation.INavigationViewModel;
                void RegisterSingleton<TView, TViewModel>()
                    where TView : global::Avalonia.Controls.Control, new()
                    where TViewModel : class, ElegantSeries.Flow.Navigation.INavigationViewModel;
            }
        }
        """;

    private const string WpfControlsStub = """
        namespace System.Windows
        {
            public class FrameworkElement { }
        }

        namespace System.Windows.Controls
        {
            public class UserControl : System.Windows.FrameworkElement { }
        }
        """;

    private const string WpfBaseViewStub = """
        namespace ElegantSeries.Flow.WPF.Views
        {
            public abstract class BaseView<TViewModel> : System.Windows.Controls.UserControl
                where TViewModel : class, ElegantSeries.Flow.Navigation.INavigationViewModel
            {
            }
        }
        """;

    private const string WpfLocatorStub = """
        namespace ElegantSeries.Flow.WPF.Locating
        {
            public interface IViewLocator
            {
                void Register<TView, TViewModel>()
                    where TView : System.Windows.FrameworkElement, new()
                    where TViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel;
                void RegisterTransient<TView, TViewModel>()
                    where TView : System.Windows.FrameworkElement, new()
                    where TViewModel : class, ElegantSeries.Flow.Navigation.INavigationViewModel;
                void RegisterSingleton<TView, TViewModel>()
                    where TView : System.Windows.FrameworkElement, new()
                    where TViewModel : class, ElegantSeries.Flow.Navigation.INavigationViewModel;
            }
        }
        """;

    private const string ViewsWithRootGeneratedType = """
        namespace TestCompilation
        {
            public class Generated
            {
            }

            public class ProbeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }

            [ElegantSeries.Flow.Routing.ViewFor<TestCompilation.ProbeViewModel>]
            public class ProbeView : Avalonia.Controls.Control
            {
            }
        }
        """;

    private const string BasicViews = """
        namespace TestApp.ViewModels
        {
            public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }

            public class MenuViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }

            public class DetailViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
        }

        namespace TestApp.Views
        {
            [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
            public class HomeView : Avalonia.Controls.Control { }

            [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.MenuViewModel>(
                Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.Singleton)]
            public class MenuView : Avalonia.Controls.Control { }

            [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.DetailViewModel>(
                Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.ViewOnly)]
            public class DetailView : Avalonia.Controls.Control { }
        }
        """;

    [Fact]
    public void EmitsRegistrationsForAttributedViews()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + BasicViews;
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

        // The generated method's documentation carries the DI-backed
        // registration requirement (the locator-without-services trap).
        Assert.Contains(
            "Registrations whose <c>Lifetime</c> selects a DI-backed registration",
            text);
        Assert.Contains(
            "service collection they throw <c>InvalidOperationException</c>.",
            text);

        // The generated code must compile against the stub locator.
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void DuplicateViewModel_ReportsFlowGen001()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.Control { }

                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class OtherHomeView : Avalonia.Controls.Control { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN001");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    [Fact]
    public void BothFormsOnOneView_SameViewModel_ReportsFlowGen013()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel> { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN013");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        // The duplicate-view diagnostic replaces, not stacks with, FLOWGEN001.
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN001");
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void BothFormsOnOneView_ConflictingViewModels_ReportsFlowGen005Not013()
    {
        // The generic declaration disagrees with the BaseView-inferred type:
        // that is FLOWGEN005's territory (explicit vs. inferred conflict on
        // the generic application itself). The errored mapping is excluded
        // from the duplicate-declaration check, so FLOWGEN013 does not stack.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }

                public class OtherViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.OtherViewModel>]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel> { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN005");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN013");
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void MultipleErrors_AllReportedTogether()
    {
        // Two views with unresolvable problems: both diagnostics must be reported
        // in a single compilation, not one at a time.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + WpfLocatorStub + BasicViews;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN002");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void NoPlatformReferenced_WithValidMappings_ReportsFlowGen014()
    {
        // The mappings are valid; the root cause is the missing platform
        // reference itself, so the error fires anyway: without a locator no
        // registration can ever be generated for this compilation.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + BasicViews;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN014");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void NoPlatformReferenced_WithInvalidMapping_ReportsOnlyFlowGen014()
    {
        // The plain [ViewFor] below would be FLOWGEN004 with a platform
        // referenced; without one, the single platform error replaces the
        // per-mapping reports (the same cascade discipline as FLOWGEN002).
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics);
        Assert.Equal("FLOWGEN014", error.Id);
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void NoPlatformReferenced_WithoutMappings_StaysSilent()
    {
        // No attributed views of its own: the provider-only path, which has
        // no declared registration intent to report on.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics);
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void NoPlatformReferenced_WithReferencedProvider_StaysSilent()
    {
        // The scope exclusion with a provider actually referenced: this
        // compilation declares no views of its own and references a validated
        // provider library, but no platform locator either (no
        // FlowStubsReference) — the provider-only path stays silent and emits
        // no aggregator.
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            namespace TestApp
            {
                public class Placeholder { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { plugin });

        Assert.Empty(diagnostics);
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void ValidLifetimeCast_EmitsCorrectRegistration()
    {
        // Guards the fix for a second silent bug in the old syntax-based parsing:
        // a *valid* integral cast such as (ViewModelLifetime)1 was downgraded to
        // Transient because only MemberAccessExpressionSyntax was recognized.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class MenuViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.MenuViewModel>(
                    Lifetime = (ElegantSeries.Flow.Routing.ViewModelLifetime)1)]
                public class MenuView : Avalonia.Controls.Control { }
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>(
                    Lifetime = (ElegantSeries.Flow.Routing.ViewModelLifetime)99)]
                public class HomeView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN003");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void NoPlatformLocator_ReportsFlowGen014_AndEmitsNothing()
    {
        var sources = AttributeStub + BasicViews;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN014");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InfersViewModelFromBaseView()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
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
    public void OpenGenericViewWithParameterlessViewFor_ReportsFlowGen008Only()
    {
        // GenericView<T> DOES inherit BaseView<T> — the base class's type
        // argument is the view's own type parameter, so no ViewModel can
        // be inferred from it. Only FLOWGEN008 may be reported: FLOWGEN004
        // must not accompany it, because its message claims the view does
        // not inherit BaseView<TViewModel>, which is false here.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class GenericView<T> : ElegantSeries.Flow.Avalonia.Views.BaseView<T>
                    where T : class, ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN008");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN004");
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void ConsumerShapedProject_NestedNamespaceInlineQualifiedBaseView_CompilesClean()
    {
        // A consumer-shaped project (assembly name unrelated to the brand):
        // the view sits in a nested consumer namespace and names its library
        // base class by its full ElegantSeries.Flow.* name inline, without a
        // using directive. The generated namespace must not hijack that
        // resolution: whatever segments it plants under the consumer root,
        // an ElegantSeries.* lookup from inside the consumer's own namespaces
        // must still reach the library.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace ShopApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace ShopApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<ShopApp.ViewModels.HomeViewModel>
                {
                }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources, assemblyName: "ShopApp");

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
    }

    [Fact]
    public void TypeParameterViewModelType_ReportsFlowGen006()
    {
        // A type argument that is not a named type (here the view's own type
        // parameter) cannot be resolved to a registrable ViewModel: FLOWGEN006
        // instead of silently skipping the view. (This is the only shape that
        // reaches FLOWGEN006 now that the explicit form is generic-only.)
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<T>]
                public class HomeView<T> : Avalonia.Controls.Control
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
                public class OtherViewModel { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.OtherViewModel>]
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
            {
                public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
            }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
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

    [Fact]
    public void PocoView_ReportsFlowGen011()
    {
        // A view that does not derive from the platform base type (Control)
        // violates the locator's TView constraint: FLOWGEN011 must fire instead
        // of a CS0311 inside the generated file.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void UnchangedMapping_ReparsedTree_ReusesGeneratedOutput()
    {
        // Incrementality: the pipeline model compares by value, so
        // when a compilation change re-runs the mapping transform on
        // an identical (reparsed) view file — producing brand-new
        // mapping objects with identical payloads — the output step
        // still sees unchanged inputs and the driver reuses the
        // previously generated source verbatim. With reference
        // equality on the model, the same reparse marked every
        // mapping "changed" and re-ran generation in full.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + BasicViews;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(sources) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ViewForGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var firstSource = Assert.Single(
            driver.GetRunResult().Results.Single().GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");

        // Replace the tree with a reparse of the same text and add an
        // unrelated tree: every syntax node is a new object, the
        // mapping payloads are unchanged.
        var oldTree = compilation.SyntaxTrees.Single();
        var reparsed = CSharpSyntaxTree.ParseText(sources);
        var changed = compilation
            .ReplaceSyntaxTree(oldTree, reparsed)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Unrelated { public class Marker { } }"));
        driver = driver.RunGeneratorsAndUpdateCompilation(changed, out _, out _);
        var secondSource = Assert.Single(
            driver.GetRunResult().Results.Single().GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");

        Assert.Same(firstSource.SourceText, secondSource.SourceText);
    }

    [Fact]
    public void RecordView_ReportsFlowGen011()
    {
        // A record class cannot derive from the platform view base type
        // (a record's base can only be object or another record), so it
        // can never be registered as a view. The syntax predicate used
        // to match class declarations only, so the attribute on a
        // record was silently ignored — no registration, no diagnostic.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public record HomeView { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("record", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void RecordView_PlainViewFor_ReportsFlowGen011Only()
    {
        // The plain form on a record: the record violation names the
        // real problem, so the FLOWGEN004 inference failure — whose
        // message would blame the missing BaseView<T> inheritance the
        // record can never have — must not stack on top of it.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public record HomeView { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics);
        Assert.Equal("FLOWGEN011", error.Id);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void OpenGenericRecordView_ReportsFlowGen008Only()
    {
        // An open generic record is rejected by FLOWGEN008 like any
        // open generic view; the record violation must not stack on
        // top of it (constraint questions about an unbound type are
        // derivative — the same suppression open generic classes get).
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public record GenericView<T> { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics);
        Assert.Equal("FLOWGEN008", error.Id);
    }

    [Fact]
    public void DoublyDeclaredView_PlusSecondViewForSameViewModel_ReportsFlowGen013And001()
    {
        // The doubly-declared view's extra declaration is excluded
        // from the duplicate-ViewModel check (FLOWGEN013 covers it),
        // but its first declaration still counts: a second view
        // claiming the same ViewModel must trip FLOWGEN001. When
        // ViewMapping gained value equality, a HashSet<ViewMapping>
        // could no longer tell the first declaration from the
        // value-equal extra one, excluded both, and the FLOWGEN001
        // was silently dropped.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel> { }

                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class OtherView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel> { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN013");
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN001");
    }

    [Fact]
    public void AbstractView_ReportsFlowGen011()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public abstract class HomeView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void ViewWithoutPublicParameterlessConstructor_ReportsFlowGen011()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.Control
                {
                    public HomeView(int unused) { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void PrivateNestedView_ReportsFlowGen011()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                public class Container
                {
                    [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                    private class NestedView : Avalonia.Controls.Control { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void AbstractBaseViewDerivedView_ReportsFlowGen011()
    {
        // The view-side checks also apply to the inferred form: deriving from
        // BaseView satisfies the platform base-type requirement, but an
        // abstract concrete view still cannot be instantiated.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public abstract class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel>
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void WpfPocoView_ReportsFlowGen011()
    {
        // The base-type requirement follows the detected platform: under the
        // WPF locator the view must derive from FrameworkElement.
        var sources = AttributeStub + WpfControlsStub + NavigationViewModelStub + WpfLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void WpfView_EmitsRegistration()
    {
        var sources = AttributeStub + WpfControlsStub + NavigationViewModelStub + WpfLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : System.Windows.FrameworkElement { }
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
    public void StructViewModel_Transient_ReportsFlowGen012()
    {
        // RegisterTransient requires a reference-type ViewModel; a struct
        // ViewModel must produce FLOWGEN012 instead of a CS0452 inside the
        // generated file.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public struct StructViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.StructViewModel>]
                public class StructView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN012");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void StructViewModel_Singleton_ReportsFlowGen012()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public struct StructViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.StructViewModel>(
                    Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.Singleton)]
                public class StructView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN012");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void StructViewModel_ViewOnly_EmitsRegistration()
    {
        // Register (ViewOnly) has no reference-type requirement on the
        // ViewModel: a struct ViewModel is legal and must not be reported.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public struct StructViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.StructViewModel>(
                    Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.ViewOnly)]
                public class StructView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.Register<global::TestApp.Views.StructView, global::TestApp.ViewModels.StructViewModel>();",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }


    [Fact]
    public void FileLocalView_ReportsFlowGen011()
    {
        // A file-local view cannot be referenced from the generated file.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                file class FileView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void PrivateNestedViewModel_ReportsFlowGen017()
    {
        // The ViewModel is a private nested class: the attribute site
        // compiles (it sits inside the containing class), but the
        // generated registration code cannot name the type.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                public class Container
                {
                    private class SecretViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                    {
                        public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                    }

                    [ElegantSeries.Flow.Routing.ViewFor<SecretViewModel>]
                    public class NestedView : Avalonia.Controls.Control { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("not accessible", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void FileLocalViewModel_ReportsFlowGen017()
    {
        // A file-local ViewModel cannot be referenced from the
        // generated file.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                file class LocalViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.LocalViewModel>]
                public class LocalView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("file-local", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void GenericNestedViewModel_ReportsFlowGen017()
    {
        // A ViewModel nested in a generic type cannot be named by the
        // generated registration code.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class Container<T>
                {
                    public class NestedViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                    {
                        public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                    }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.Container<int>.NestedViewModel>]
                public class NestedView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("nested in a generic type", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InferredPrivateNestedViewModel_ReportsFlowGen017()
    {
        // The inferred path is checked too: the view infers its
        // ViewModel from BaseView<TViewModel>, and that ViewModel is a
        // private nested class of the shared container.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.Views
            {
                public class Container
                {
                    private class SecretViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                    {
                        public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                    }

                    [ElegantSeries.Flow.Routing.ViewFor]
                    public class NestedView : ElegantSeries.Flow.Avalonia.Views.BaseView<SecretViewModel> { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("not accessible", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void GenericViewModelWithPrivateNestedTypeArgument_ReportsFlowGen017()
    {
        // The ViewModel type itself is public, but the generated code
        // writes its type argument out in full: a private nested type
        // argument is just as unnameable as a private nested ViewModel.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class DetailViewModel<T> : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                public class Container
                {
                    private class Secret { }

                    [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.DetailViewModel<Secret>>]
                    public class DetailView : Avalonia.Controls.Control { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("not accessible", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void GenericViewModelWithFileLocalTypeArgument_ReportsFlowGen017()
    {
        // A file-local type argument cannot be referenced from the
        // generated file, exactly like a file-local ViewModel.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class DetailViewModel<T> : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }

                file class LocalDto { }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.DetailViewModel<TestApp.ViewModels.LocalDto>>]
                public class DetailView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("file-local", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void GenericViewModelWithGenericNestedTypeArgument_ReportsFlowGen017()
    {
        // A type argument nested in a generic type carries an unbound
        // type parameter in its fully qualified name; the generated code
        // cannot name it.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class Container<T>
                {
                    public class NestedDto { }
                }

                public class DetailViewModel<T> : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.DetailViewModel<TestApp.ViewModels.Container<int>.NestedDto>>]
                public class DetailView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("nested in a generic type", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InferredGenericViewModelWithPrivateNestedTypeArgument_ReportsFlowGen017()
    {
        // The inferred path is checked too: the type argument of the
        // BaseView-inferred generic ViewModel is a private nested class.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public class DetailViewModel<T> : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                public class Container
                {
                    private class Secret { }

                    [ElegantSeries.Flow.Routing.ViewFor]
                    public class DetailView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.DetailViewModel<Secret>> { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("not accessible", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void AbstractViewModel_ReportsFlowGen017()
    {
        // RegisterTransient registers the ViewModel type itself with the
        // service collection; an abstract ViewModel can never be
        // instantiated by the container, so the mapping is rejected at
        // compile time instead of failing when navigation resolves it.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public abstract class AbstractViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.AbstractViewModel>]
                public class AbstractView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("abstract", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InterfaceViewModel_Singleton_ReportsFlowGen017()
    {
        // Same instantiability requirement under RegisterSingleton, and
        // the message names the interface itself (an interface symbol
        // also reports IsAbstract, so a generic "abstract" message here
        // would misdescribe the problem).
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public interface IDetailViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.IDetailViewModel>(
                    Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.Singleton)]
                public class DetailView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("interface", error.GetMessage());
        Assert.DoesNotContain("abstract", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InferredAbstractViewModel_ReportsFlowGen017()
    {
        // The inferred path is checked too: BaseView<TViewModel>'s class
        // constraint admits an abstract ViewModel, so the generator is
        // the only compile-time line of defense for this form.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public abstract class AbstractViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class AbstractView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.AbstractViewModel> { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("abstract", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InterfaceViewModel_ViewOnly_EmitsRegistration()
    {
        // Register (ViewOnly) registers no ViewModel: the user registers
        // the interface with an implementation themselves, so an
        // interface ViewModel is legitimate and must not be reported.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public interface IDetailViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.IDetailViewModel>(
                    Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.ViewOnly)]
                public class DetailView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.Register<global::TestApp.Views.DetailView, global::TestApp.ViewModels.IDetailViewModel>();",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void AbstractViewModel_ViewOnly_EmitsRegistration()
    {
        // The ViewOnly exemption also covers an abstract ViewModel: the
        // user may register it with a concrete implementation themselves.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public abstract class AbstractViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.AbstractViewModel>(
                    Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.ViewOnly)]
                public class AbstractView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.Register<global::TestApp.Views.AbstractView, global::TestApp.ViewModels.AbstractViewModel>();",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void InterfaceViewModel_ReportsFlowGen017()
    {
        // The interface arm under the default (Transient) lifetime; the
        // Singleton permutation is pinned by
        // InterfaceViewModel_Singleton_ReportsFlowGen017.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public interface IDetailViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.IDetailViewModel>]
                public class DetailView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("interface", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void AbstractViewModel_Singleton_ReportsFlowGen017()
    {
        // The abstract arm under RegisterSingleton; the Transient
        // permutation is pinned by AbstractViewModel_ReportsFlowGen017.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public abstract class AbstractViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.AbstractViewModel>(
                    Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.Singleton)]
                public class AbstractView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("abstract", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InferredInterfaceViewModel_ReportsFlowGen017()
    {
        // The interface arm on the inferred path: BaseView<TViewModel>'s
        // class constraint admits an interface ViewModel.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub + """
            namespace TestApp.ViewModels
            {
                public interface IDetailViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class DetailView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.IDetailViewModel> { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("interface", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void NoPublicConstructorViewModel_ReportsFlowGen017()
    {
        // The container activates a Transient/Singleton ViewModel through
        // a public constructor; a concrete ViewModel whose only
        // constructor is private compiles into the registration and fails
        // when navigation first resolves it, so it is rejected here.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class NoCtorViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    private NoCtorViewModel() { }

                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.NoCtorViewModel>]
                public class NoCtorView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN017");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("public constructor", error.GetMessage());
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void NoPublicConstructorViewModel_ViewOnly_EmitsRegistration()
    {
        // Under ViewOnly the user controls how the ViewModel is created
        // (for example a factory registration), so a ViewModel with no
        // public constructor is legitimate and must not be reported.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class NoCtorViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    private NoCtorViewModel() { }

                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.NoCtorViewModel>(
                    Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.ViewOnly)]
                public class NoCtorView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.Register<global::TestApp.Views.NoCtorView, global::TestApp.ViewModels.NoCtorViewModel>();",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ParameterizedConstructorViewModel_EmitsRegistration()
    {
        // Unlike the view side (new() constraint), a Transient ViewModel
        // needs no parameterless constructor: any public constructor
        // qualifies, because the container resolves its parameters.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class InjectedViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public InjectedViewModel(string name) { }

                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.InjectedViewModel>]
                public class InjectedView : Avalonia.Controls.Control { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "views.RegisterTransient<global::TestApp.Views.InjectedView, global::TestApp.ViewModels.InjectedViewModel>();",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ViewNestedInFileLocalType_ReportsFlowGen011()
    {
        // The view itself is not file-local, but its containing type is:
        // the generated file still cannot reference it.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                file class Container
                {
                    [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                    public class InnerView : Avalonia.Controls.Control { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void ViewNestedInGenericType_ReportsFlowGen011()
    {
        // The fully qualified name of a view nested in a generic type contains
        // an unbound type parameter; the generated code cannot name it.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                public class Outer<T>
                {
                    [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                    public class InnerView : Avalonia.Controls.Control { }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN011");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void StaticView_ReportsFlowGen011_WithoutConstructorReason()
    {
        // A static view groups with abstract views: the derivation and
        // abstract reasons fire, the derivative constructor reason is
        // suppressed (its implicit constructor is not public by construction).
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public static class StaticView { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        var errors = diagnostics.Where(d => d.Id == "FLOWGEN011").ToArray();
        Assert.Equal(2, errors.Length);
        Assert.DoesNotContain(errors, d => d.GetMessage().Contains("parameterless"));
    }

    [Fact]
    public void ViewWithUnresolvableBaseType_DoesNotReportFlowGen011()
    {
        // When the base chain contains an error type (a typo'd base class),
        // derivation from the platform base is unknowable and must not be
        // asserted; the compiler already reports the user's own error.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class BrokenView : TypoBase { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN011");
    }

    [Fact]
    public void GeneratedNamespace_IncludesTheAssemblyName()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + BasicViews;
        var (runResult, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "namespace ElegantSeries.Flow.Generated.TestCompilation",
            generated.SourceText.ToString());
    }

    [Fact]
    public void GeneratedNamespace_SanitizesAssemblyNameIntoValidIdentifierSegments()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + BasicViews;
        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources, "2-cool.namespace");

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // '2-cool' -> '_2_cool' (dash replaced, leading digit prefixed) and the
        // reserved keyword 'namespace' -> 'namespace_'.
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "namespace ElegantSeries.Flow.Generated._2_cool.namespace_",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GeneratedNamespace_RootTypeNamedGenerated_DoesNotCollide()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + ViewsWithRootGeneratedType;
        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // The consumer's own 'Generated' type sits in the root namespace; the
        // generated namespace must not reuse that segment name (CS0101).
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "namespace ElegantSeries.Flow.Generated.TestCompilation",
            generated.SourceText.ToString());
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GeneratedNamespace_EmptySegmentAndMissingNameFallbacks()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + BasicViews;

        // An empty trailing segment becomes '_'.
        var (dotRunResult, dotDiagnostics, _) = RunGenerator(sources, "Foo.");
        Assert.Empty(dotDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var dotGenerated = Assert.Single(
            dotRunResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "namespace ElegantSeries.Flow.Generated.Foo._",
            dotGenerated.SourceText.ToString());

        // A whitespace-only assembly name falls back to the fixed Unknown
        // segment under the generated layer.
        // (The compilation itself is invalid — CS8203 — so only the emitted
        // namespace line is pinned here, not the updated compilation.)
        var (blankRunResult, blankDiagnostics, _) = RunGenerator(sources, " ");
        Assert.Empty(blankDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var blankGenerated = Assert.Single(
            blankRunResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        var blankText = blankGenerated.SourceText.ToString();
        Assert.Contains("namespace ElegantSeries.Flow.Generated.Unknown", blankText);
        Assert.DoesNotContain("namespace ElegantSeries.Flow.Generated.Unknown.", blankText);
    }

    [Fact]
    public void OwnGeneratedFile_CarriesRegistrationsMarker()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Contains(
            "[assembly: global::ElegantSeries.Flow.Routing.GeneratedViewRegistrations(\"ElegantSeries.Flow.Generated.TestCompilation.ViewForRegistrations\")]",
            generated.SourceText.ToString());
    }

    [Fact]
    public void ReferencedProvider_GeneratesAggregatorWithOwnCallFirst()
    {
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        var text = aggregator.SourceText.ToString();
        var ownCall = "global::ElegantSeries.Flow.Generated.TestCompilation.ViewForRegistrations.RegisterAttributedViews(views);";
        var providerCall = "global::ElegantSeries.Flow.Generated.Plugin.ViewForRegistrations.RegisterAttributedViews(views);";
        Assert.Contains(ownCall, text);
        Assert.Contains(providerCall, text);
        Assert.True(
            text.IndexOf(ownCall, System.StringComparison.Ordinal) < text.IndexOf(providerCall, System.StringComparison.Ordinal),
            "The aggregation must call this assembly's own registrations before the providers'.");

        // The aggregator's documentation carries the same DI-backed
        // registration requirement as the per-assembly method it calls.
        Assert.Contains(
            "Registrations whose <c>Lifetime</c> selects a DI-backed registration",
            text);
        Assert.Contains(
            "service collection they throw <c>InvalidOperationException</c>.",
            text);
    }

    [Fact]
    public void NoProviderReferences_NoAggregatorGenerated()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
    }

    [Fact]
    public void ProviderOnlyApp_GeneratesAggregatorWithoutOwnCall()
    {
        // The desktop-shell shape: the app itself declares no views; every
        // view arrives from a referenced module. The aggregation must still
        // be generated, without a call to an own registrations class that
        // does not exist.
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        var text = aggregator.SourceText.ToString();
        Assert.Contains("global::ElegantSeries.Flow.Generated.Plugin.ViewForRegistrations.RegisterAttributedViews(views);", text);
        Assert.DoesNotContain("ElegantSeries.Flow.Generated.TestCompilation.ViewForRegistrations.RegisterAttributedViews", text);
    }

    [Fact]
    public void ProviderWithWrongRegistrationSignature_IsSkipped()
    {
        // A marker alone is not enough: a "provider" whose registration
        // method does not take this compilation's platform locator is not a
        // real provider (forged marker or incompatible generator output) and
        // must be silently skipped rather than called.
        var impostor = BuildProviderLibrary(
            "Impostor",
            "Impostor",
            "FakeView",
            "FakeViewModel",
            registrationsOverride: """
                [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations]

                namespace ElegantSeries.Flow.Generated.Impostor
                {
                    public static class ViewForRegistrations
                    {
                        public static void RegisterAttributedViews(string notAViewLocator)
                        {
                        }
                    }
                }
                """);
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, impostor });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
    }

    [Fact]
    public void ProviderWithInternalRegistrationType_IsSkipped()
    {
        // The aggregator calls the provider through a fully qualified static
        // call from another assembly, so a registration type that is not
        // public can never be a real provider: validation must reject it
        // instead of emitting a call that cannot bind (CS0122).
        var cloaked = BuildProviderLibrary(
            "Cloaked",
            "Cloaked",
            "HiddenView",
            "HiddenViewModel",
            registrationsOverride: """
                [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations]

                namespace ElegantSeries.Flow.Generated.Cloaked
                {
                    internal static class ViewForRegistrations
                    {
                        public static void RegisterAttributedViews(this global::ElegantSeries.Flow.Avalonia.Locating.IViewLocator views)
                        {
                        }
                    }
                }
                """);
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, cloaked });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
    }

    [Fact]
    public void ProviderWithKeywordNamespaceSegment_IsSkipped()
    {
        // A hand-forged marker can record a registrations type inside a
        // keyword namespace segment (declared as namespace My.@event):
        // the type resolves through metadata names and passes the shape
        // check, but its full name cannot be written into the aggregator's
        // C# source — emitting it verbatim would produce a bare compiler
        // error in the generated file. Recognition must reject it like
        // any other unbindable shape.
        var forged = BuildProviderLibrary(
            "Forged",
            "Forged",
            "ForgedView",
            "ForgedViewModel",
            registrationsOverride: """
                [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations("My.event.ViewForRegistrations")]

                namespace My.@event
                {
                    public static class ViewForRegistrations
                    {
                        public static void RegisterAttributedViews(this global::ElegantSeries.Flow.Avalonia.Locating.IViewLocator views)
                        {
                        }
                    }
                }
                """);
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, forged });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ProviderWithGenericRegistrationMethod_IsSkipped()
    {
        // A generic registration method cannot be called without type
        // arguments the aggregator does not have (CS0411), so it fails
        // validation like any other non-conforming shape.
        var generic = BuildProviderLibrary(
            "Generic",
            "Generic",
            "OpenView",
            "OpenViewModel",
            registrationsOverride: """
                [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations]

                namespace ElegantSeries.Flow.Generated.Generic
                {
                    public static class ViewForRegistrations
                    {
                        public static void RegisterAttributedViews<T>(this global::ElegantSeries.Flow.Avalonia.Locating.IViewLocator views)
                        {
                        }
                    }
                }
                """);
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, generic });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
    }

    [Fact]
    public void MultipleProviders_SortKeyIsRawAssemblyName()
    {
        // "Z-Mod" sorts before "Z.Mod" by raw assembly name ('-' 0x2D <
        // '.' 0x2E), but after it by sanitized type name ("ElegantSeries.Flow.Generated.Z.Mod" vs
        // "ElegantSeries.Flow.Generated.Z_Mod": '.' 0x2E < '_' 0x5F). The aggregation order is the raw
        // assembly-name order.
        var hyphen = BuildProviderLibrary("Z-Mod", "ZMod", "AlphaView", "AlphaViewModel");
        var dotted = BuildProviderLibrary("Z.Mod", "ZDotMod", "BetaView", "BetaViewModel");
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, dotted, hyphen });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        var text = aggregator.SourceText.ToString();
        var hyphenCall = "global::ElegantSeries.Flow.Generated.Z_Mod.ViewForRegistrations.RegisterAttributedViews(views);";
        var dottedCall = "global::ElegantSeries.Flow.Generated.Z.Mod.ViewForRegistrations.RegisterAttributedViews(views);";
        Assert.Contains(hyphenCall, text);
        Assert.Contains(dottedCall, text);
        Assert.True(
            text.IndexOf(hyphenCall, System.StringComparison.Ordinal) < text.IndexOf(dottedCall, System.StringComparison.Ordinal),
            "Provider calls must be sorted by raw assembly name, not by sanitized type name.");
    }

    [Fact]
    public void ProviderOnlyApp_WithAmbiguousPlatforms_ReportsFlowGen002()
    {
        // Both platform locators visible (the harness inlines both locator
        // stubs; FlowStubs also carries the Avalonia one — a harness
        // artifact) and a genuine provider referenced: the provider-only
        // path must report FLOWGEN002 rather than guess a platform.
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = AttributeStub + AvaloniaLocatorStub + WpfLocatorStub + """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN002");
    }

    [Fact]
    public void ProviderOnlyApp_InvalidNamespaceOverrideWithAmbiguousPlatforms_ReportsFlowGen016NotFlowGen002()
    {
        // The invalid-override report precedes the platform-count
        // judgement on every path: a provider-only compilation with both
        // locators visible and a validated provider hears FLOWGEN016,
        // not FLOWGEN002 (the ambiguity surfaces once the override is
        // fixed).
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = "[assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace(\"1bad\")]\n" +
            AttributeStub + AvaloniaLocatorStub + WpfLocatorStub + """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN016");
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN002");
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void InferredViewModel_FromBaseViewOutsideLocatorAssembly_IsNotInferred()
    {
        // A same-named BaseView defined in a different assembly than the
        // platform locator is not the framework base class: it carries
        // no INavigationViewModel constraint guarantee, so nothing is
        // inferred from it and the parameterless attribute reports
        // FLOWGEN004 instead of emitting an uncompilable registration.
        var foreignPlatform = BuildStubLibrary(
            "Contoso.Platform",
            AvaloniaControlsStub + NavigationViewModelStub + AvaloniaBaseViewStub);
        var sources = AttributeStub + AvaloniaLocatorStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel> { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { foreignPlatform });
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN004");
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void InferredViewModel_FromBaseViewInLocatorAssembly_IsInferred()
    {
        // The positive counterpart: BaseView and the locator living in
        // the one referenced platform assembly (the production layout)
        // infer as before.
        var platform = BuildStubLibrary(
            "Contoso.Platform",
            AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + AvaloniaBaseViewStub);
        var sources = AttributeStub + """
            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor]
                public class HomeView : ElegantSeries.Flow.Avalonia.Views.BaseView<TestApp.ViewModels.HomeViewModel> { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { platform });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(runResult.GeneratedSources, s => s.HintName == "ViewForRegistrations.g.cs");
    }

    /// <summary>
    /// Builds a small stub library (any of the shared stub sources) as a
    /// metadata reference under the given assembly name, so a test can
    /// place stub types in a different assembly than the compilation
    /// under test.
    /// </summary>
    private static MetadataReference BuildStubLibrary(string assemblyName, string sources)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(sources) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
        return compilation.ToMetadataReference();
    }

    [Fact]
    public void ProviderOnlyApp_AmbiguousPlatformsWithOnlyImpostorProvider_StaysSilent()
    {
        // The ambiguity report is gated on validated providers: an impostor
        // that fails validation against both candidate locators must not be
        // able to switch on a build error.
        var impostor = BuildProviderLibrary(
            "Impostor",
            "Impostor",
            "FakeView",
            "FakeViewModel",
            registrationsOverride: """
                [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations]

                namespace ElegantSeries.Flow.Generated.Impostor
                {
                    public static class ViewForRegistrations
                    {
                        public static void RegisterAttributedViews(string notAViewLocator)
                        {
                        }
                    }
                }
                """);
        var sources = AttributeStub + AvaloniaLocatorStub + WpfLocatorStub + """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, impostor });
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN002");
    }

    [Fact]
    public void ErrorMapping_SuppressesAggregatorAlongWithRegistrations()
    {
        // A FLOWGEN error in the app's own mappings cancels all generation
        // for the compilation, the aggregation included — even when a valid
        // provider is referenced.
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.BadViewModel>]
                public class BadView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class BadViewModel { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN010");
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void MultipleProviders_AggregatorCallsSortedByAssemblyName()
    {
        var reports = BuildProviderLibrary("Reports", "Reports", "ChartView", "ChartViewModel");
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        // References are deliberately supplied in reverse name order; the
        // aggregation order must not depend on reference order.
        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, reports, plugin });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        var text = aggregator.SourceText.ToString();
        var pluginCall = "global::ElegantSeries.Flow.Generated.Plugin.ViewForRegistrations.RegisterAttributedViews(views);";
        var reportsCall = "global::ElegantSeries.Flow.Generated.Reports.ViewForRegistrations.RegisterAttributedViews(views);";
        Assert.Contains(pluginCall, text);
        Assert.Contains(reportsCall, text);
        Assert.True(
            text.IndexOf(pluginCall, System.StringComparison.Ordinal) < text.IndexOf(reportsCall, System.StringComparison.Ordinal),
            "Provider calls must be sorted by assembly name for deterministic output.");
    }

    [Fact]
    public void ProviderRegistration_CallerCanComposeViaQualifiedStaticCall()
    {
        // The manual composition contract pinned end to end across two
        // assemblies: without using the aggregation, the composition root
        // calls the provider's registrations through the fully qualified
        // static form and the whole compilation stays clean.
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            namespace TestApp
            {
                public static class Composition
                {
                    public static void RegisterModuleViews(ElegantSeries.Flow.Avalonia.Locating.IViewLocator views)
                    {
                        global::ElegantSeries.Flow.Generated.Plugin.ViewForRegistrations.RegisterAttributedViews(views);
                    }
                }
            }
            """;

        var (_, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void UserTypeInsideGeneratedNamespace_CoexistsWithRegistrations()
    {
        // A namespace is not a type: user code may live in the same
        // ElegantSeries.Flow.Generated.<assembly> namespace as the generated
        // registrations without colliding with them.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace ElegantSeries.Flow.Generated.TestCompilation
            {
                public static class UsersOwnHelpers { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(sources);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(runResult.GeneratedSources, s => s.HintName == "ViewForRegistrations.g.cs");
    }

    [Fact]
    public void OwnTypeNamedGeneratedInLibraryRoot_CollidesWithGeneratedLayer()
    {
        // The accepted residual boundary of the umbrella derivation: the
        // generated registrations live under the library's root namespace,
        // in the ElegantSeries.Flow.Generated layer. A consumer who
        // declares their own type named Generated inside the
        // ElegantSeries.Flow namespace of their own assembly cannot
        // coexist with that layer's namespace in the same compilation
        // (CS0101, reported on the consumer's own declaration). Renaming
        // or relocating that one type is the remedy; this pins the
        // boundary so it is a recorded cost, not a surprise. (The library
        // side of the same boundary — no library type named Generated, no
        // library types in the layer — is pinned by the guard tests in
        // ElegantSeries.Flow.Tests.)
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }

            namespace ElegantSeries.Flow
            {
                public class Generated { }
            }
            """;

        var (_, _, updatedCompilation) = RunGenerator(sources);
        Assert.Contains(
            updatedCompilation.GetDiagnostics(),
            d => d.Id == "CS0101" && d.Severity == DiagnosticSeverity.Error);
    }

    private static (GeneratorRunResult RunResult, ImmutableArray<Diagnostic> Diagnostics, Compilation UpdatedCompilation)
        RunGenerator(
            string sources,
            string assemblyName = "TestCompilation",
            IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sources);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ViewForGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        var runResult = driver.GetRunResult();

        return (runResult.Results.Single(), runResult.Diagnostics, updatedCompilation);
    }

    /// <summary>
    /// The shared stub assembly (attributes, controls, navigation contracts,
    /// Avalonia locator) as a metadata reference. Provider libraries and the
    /// app compilations that consume them must all resolve the stub types —
    /// above all the platform locator — from this one assembly, exactly as
    /// real assemblies all reference the one platform package; compiling the
    /// stubs into each test compilation instead would give every compilation
    /// its own private copy of the locator interface and the cross-assembly
    /// calls would not type-check.
    /// </summary>
    private static readonly MetadataReference FlowStubsReference = BuildFlowStubs();

    private static MetadataReference BuildFlowStubs()
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "ElegantSeries.Flow.Stubs",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub),
            },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
        return compilation.ToMetadataReference();
    }

    // Mirrors the generator's SanitizeNamespaceRoot rule for rule (invalid
    // characters to '_', empty segment to '_', '_' prefix for a leading
    // digit, '_' suffix for a reserved keyword): the aggregator predicts a
    // payload-less provider's location with the real sanitizer, so any
    // divergence here would place the replica's registrations where
    // aggregation never looks, and the provider would silently vanish
    // from the test instead of failing in it.
    private static string SanitizeReplica(string assemblyName)
    {
        var segments = assemblyName.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = new string(segments[i]
                .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
                .ToArray());
            if (segment.Length == 0)
            {
                segment = "_";
            }
            else if (char.IsDigit(segment[0]))
            {
                segment = "_" + segment;
            }

            if (ReplicaReservedKeywords.Contains(segment))
            {
                segment += "_";
            }

            segments[i] = segment;
        }

        return string.Join(".", segments);
    }

    private static readonly HashSet<string> ReplicaReservedKeywords = new()
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

    /// <summary>
    /// Builds a stand-in provider library: an assembly with one attributed
    /// view plus a hand-written replica of the file the generator would have
    /// emitted into it (marker attribute + registrations class), returned as
    /// a metadata reference the app compilation can consume.
    /// </summary>
    private static MetadataReference BuildProviderLibrary(
        string assemblyName,
        string viewNamespace,
        string viewName,
        string viewModelName,
        string? registrationsOverride = null)
    {
        var viewSources = $$"""
            namespace {{viewNamespace}}.Views
            {
                public class {{viewName}} : Avalonia.Controls.UserControl { }
            }

            namespace {{viewNamespace}}.ViewModels
            {
                public class {{viewModelName}} : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;
        var generatedNamespace = "ElegantSeries.Flow.Generated." + SanitizeReplica(assemblyName);
        var registrations = registrationsOverride ?? $$"""
            [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations]

            namespace {{generatedNamespace}}
            {
                public static class ViewForRegistrations
                {
                    public static void RegisterAttributedViews(this global::ElegantSeries.Flow.Avalonia.Locating.IViewLocator views)
                    {
                        views.RegisterTransient<global::{{viewNamespace}}.Views.{{viewName}}, global::{{viewNamespace}}.ViewModels.{{viewModelName}}>();
                    }
                }
            }
            """;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
            FlowStubsReference,
        };
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(viewSources), CSharpSyntaxTree.ParseText(registrations) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
        return compilation.ToMetadataReference();
    }

    [Fact]
    public void OpenGenericViewModel_ReportsFlowGen009()
    {
        // [ViewFor<DetailViewModel<T>>] with an open generic ViewModel must
        // produce FLOWGEN009, not generate uncompilable registration code.
        // (The unbound form DetailViewModel<> cannot be written as a type
        // argument at all; a constructed type over the view's own type
        // parameter is how an open ViewModel is expressed in the generic form.)
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.DetailViewModel<T>>]
                public class DetailView<T>
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.DetailViewModel<string>>]
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

    [Fact]
    public void ViewModelNotImplementingInterface_ReportsFlowGen010()
    {
        // [ViewFor<PlainClass>] where PlainClass does not implement
        // INavigationViewModel must produce FLOWGEN010.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.PlainClass>]
                public class DetailView : Avalonia.Controls.Control
                {
                }
            }

            namespace TestApp.ViewModels
            {
                public class PlainClass
                {
                }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(sources);

        var error = Assert.Single(diagnostics, d => d.Id == "FLOWGEN010");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Empty(runResult.GeneratedSources.Where(s => s.HintName == "ViewForRegistrations.g.cs"));
    }

    [Fact]
    public void ViewModelImplementingInterface_DoesNotReportFlowGen010()
    {
        // A ViewModel implementing INavigationViewModel is fine.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.GoodViewModel>]
                public class DetailView : Avalonia.Controls.Control
                {
                }
            }

            namespace TestApp.ViewModels
            {
                public class GoodViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        Assert.Empty(diagnostics.Where(d => d.Id == "FLOWGEN010"));
    }

    private static string OneViewAppSources(string? assemblyAttributeLine = null)
    {
        var attribute = assemblyAttributeLine is null ? string.Empty : assemblyAttributeLine + "\n\n";
        return attribute + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.HomeViewModel>]
                public class HomeView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class HomeViewModel : ElegantSeries.Flow.Navigation.INavigationViewModel
                {
                    public ElegantSeries.Flow.Navigation.INavigationService? Navigation { get; set; }
                }
            }
            """;
    }

    [Fact]
    public void GeneratedNamespaceOverride_ReplacesGeneratedNamespaceAndMarkerPayload()
    {
        var sources = OneViewAppSources(
            "[assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace(\"Contoso.UI\")]");

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var registrations = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        var text = registrations.SourceText.ToString();
        Assert.Contains("namespace Contoso.UI", text);
        Assert.Contains(
            "[assembly: global::ElegantSeries.Flow.Routing.GeneratedViewRegistrations(\"Contoso.UI.ViewForRegistrations\")]",
            text);
        Assert.DoesNotContain("ElegantSeries.Flow.Generated.TestCompilation", text);
    }

    [Fact]
    public void GeneratedNamespaceOverride_InvalidValues_ReportFlowGen016AndEmitNothing()
    {
        foreach (var value in new[] { "", "1abc", "Contoso..UI", "Contoso.", ".Contoso", "class", "Contoso UI" })
        {
            var sources = OneViewAppSources(
                "[assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace(\"" + value + "\")]");

            var (runResult, diagnostics, _) = RunGenerator(
                sources, additionalReferences: new[] { FlowStubsReference });
            Assert.Contains(diagnostics, d => d.Id == "FLOWGEN016");
            Assert.Empty(runResult.GeneratedSources);
        }
    }

    [Fact]
    public void GeneratedNamespaceOverride_ProviderOnlyApp_AggregatorUsesOverrideNamespace()
    {
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            [assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace("Contoso.UI")]

            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        Assert.Contains("namespace Contoso.UI", aggregator.SourceText.ToString());
    }

    [Fact]
    public void GeneratedNamespaceOverride_InvalidValueOnProviderOnlyPath_ReportsFlowGen016AndEmitsNothing()
    {
        var plugin = BuildProviderLibrary("Plugin", "Plugin", "ReportView", "ReportViewModel");
        var sources = """
            [assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace("1bad")]

            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, plugin });
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN016");
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void ProviderNamespaceCollision_ReportsFlowGen015AndSkipsAggregator()
    {
        // Contoso-UI and Contoso_UI sanitize to the same generated namespace;
        // with no override and no reference aliases the aggregator cannot
        // tell their registrations apart. The outcome must be one clear
        // FLOWGEN015, not a CS0433 inside a generated file.
        var dash = BuildProviderLibrary("Contoso-UI", "ContosoDash", "ProductView", "ProductViewModel");
        var underscore = BuildProviderLibrary("Contoso_UI", "ContosoUnderscore", "OrderView", "OrderViewModel");
        var sources = """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, dash, underscore });
        var collision = Assert.Single(diagnostics, d => d.Id == "FLOWGEN015");
        var message = collision.GetMessage();
        Assert.Contains("Contoso-UI", message);
        Assert.Contains("Contoso_UI", message);
        Assert.Contains("ElegantSeries.Flow.Generated.Contoso_UI.ViewForRegistrations", message);
        Assert.Empty(runResult.GeneratedSources.Where(
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs"));
        Assert.DoesNotContain(updatedCompilation.GetDiagnostics(), d => d.Id == "CS0433");
    }

    [Fact]
    public void ProviderNamespaceCollision_ResolvedByProviderOverride_BothCallsEmitted()
    {
        var dash = BuildProviderLibrary("Contoso-UI", "ContosoDash", "ProductView", "ProductViewModel");
        var underscore = BuildProviderLibrary(
            "Contoso_UI",
            "ContosoUnderscore",
            "OrderView",
            "OrderViewModel",
            registrationsOverride: """
                [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations("Contoso.UI.ViewForRegistrations")]

                namespace Contoso.UI
                {
                    public static class ViewForRegistrations
                    {
                        public static void RegisterAttributedViews(this global::ElegantSeries.Flow.Avalonia.Locating.IViewLocator views)
                        {
                            views.RegisterTransient<global::ContosoUnderscore.Views.OrderView, global::ContosoUnderscore.ViewModels.OrderViewModel>();
                        }
                    }
                }
                """);
        var sources = """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, dash, underscore });
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN015");
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        var text = aggregator.SourceText.ToString();
        Assert.Contains(
            "global::ElegantSeries.Flow.Generated.Contoso_UI.ViewForRegistrations.RegisterAttributedViews(views);", text);
        Assert.Contains(
            "global::Contoso.UI.ViewForRegistrations.RegisterAttributedViews(views);", text);
    }

    [Fact]
    public void ProviderNamespaceCollision_ResolvedByReferenceAlias_AliasQualifiedCallEmitted()
    {
        var dash = BuildProviderLibrary("Contoso-UI", "ContosoDash", "ProductView", "ProductViewModel");
        var underscore = BuildProviderLibrary("Contoso_UI", "ContosoUnderscore", "OrderView", "OrderViewModel")
            .WithProperties(new MetadataReferenceProperties(
                aliases: ImmutableArray.Create("ContosoUnderscore")));
        var sources = """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, dash, underscore });
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN015");
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        var text = aggregator.SourceText.ToString();
        Assert.Contains("extern alias ContosoUnderscore;", text);
        Assert.Contains(
            "ContosoUnderscore::ElegantSeries.Flow.Generated.Contoso_UI.ViewForRegistrations.RegisterAttributedViews(views);",
            text);
        Assert.Contains(
            "global::ElegantSeries.Flow.Generated.Contoso_UI.ViewForRegistrations.RegisterAttributedViews(views);", text);
    }

    [Fact]
    public void ProviderWithMarkerPayloadAtUnpredictedNamespace_IsAggregated()
    {
        // The marker records where the registrations type actually is; the
        // aggregator must read it instead of deriving the location from the
        // assembly name (derivation would look for ElegantSeries.Flow.Generated.Plugin and
        // find nothing).
        var custom = BuildProviderLibrary(
            "Plugin",
            "Plugin",
            "ReportView",
            "ReportViewModel",
            registrationsOverride: """
                [assembly: ElegantSeries.Flow.Routing.GeneratedViewRegistrations("Totally.Custom.Registrations.ViewForRegistrations")]

                namespace Totally.Custom.Registrations
                {
                    public static class ViewForRegistrations
                    {
                        public static void RegisterAttributedViews(this global::ElegantSeries.Flow.Avalonia.Locating.IViewLocator views)
                        {
                            views.RegisterTransient<global::Plugin.Views.ReportView, global::Plugin.ViewModels.ReportViewModel>();
                        }
                    }
                }
                """);
        var sources = """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, custom });
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        Assert.Contains(
            "global::Totally.Custom.Registrations.ViewForRegistrations.RegisterAttributedViews(views);",
            aggregator.SourceText.ToString());
    }

    [Fact]
    public void OwnNamespaceCollidesWithProvider_ReportsFlowGen015AndKeepsOwnRegistrations()
    {
        // The app's own assembly (Shop_App) derives the same generated
        // namespace as the referenced Shop-App provider. The collision is
        // reported and the aggregator withheld, but the app's own
        // registrations file is unaffected.
        var provider = BuildProviderLibrary("Shop-App", "ShopApp", "ReportView", "ReportViewModel");

        var (runResult, diagnostics, _) = RunGenerator(
            OneViewAppSources(),
            assemblyName: "Shop_App",
            additionalReferences: new[] { FlowStubsReference, provider });
        var collision = Assert.Single(diagnostics, d => d.Id == "FLOWGEN015");
        var message = collision.GetMessage();
        Assert.Contains("Shop-App", message);
        Assert.Contains("Shop_App", message);
        Assert.Single(runResult.GeneratedSources, s => s.HintName == "ViewForRegistrations.g.cs");
        Assert.Empty(runResult.GeneratedSources.Where(
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs"));
    }

    [Fact]
    public void GeneratedNamespaceOverride_InvalidValueOnInertCompilation_StaysSilent()
    {
        // No views of its own and no referenced providers: nothing would be
        // generated, so an invalid override must not switch on a build
        // error — the same gating the ambiguous-platform report follows.
        var sources = """
            [assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace("1bad")]

            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference });
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN016");
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void ProviderNamespaceCollision_BothProvidersAliasedDifferently_BothCallsEmitted()
    {
        // Same generated type name on both sides, but each reference
        // carries its own alias: the (name, qualifier) groups are distinct,
        // so there is no collision — the aggregator declares both extern
        // aliases and qualifies each call with its own.
        var dash = BuildProviderLibrary("Contoso-UI", "ContosoDash", "ProductView", "ProductViewModel")
            .WithProperties(new MetadataReferenceProperties(
                aliases: ImmutableArray.Create("ContosoDash")));
        var underscore = BuildProviderLibrary("Contoso_UI", "ContosoUnderscore", "OrderView", "OrderViewModel")
            .WithProperties(new MetadataReferenceProperties(
                aliases: ImmutableArray.Create("ContosoUnderscore")));
        var sources = """
            namespace TestApp
            {
                public class ShellMarker { }
            }
            """;

        var (runResult, diagnostics, updatedCompilation) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference, dash, underscore });
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN015");
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var aggregator = Assert.Single(
            runResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrationsAggregator.g.cs");
        var text = aggregator.SourceText.ToString();
        Assert.Contains("extern alias ContosoDash;", text);
        Assert.Contains("extern alias ContosoUnderscore;", text);
        Assert.Contains(
            "ContosoDash::ElegantSeries.Flow.Generated.Contoso_UI.ViewForRegistrations.RegisterAttributedViews(views);",
            text);
        Assert.Contains(
            "ContosoUnderscore::ElegantSeries.Flow.Generated.Contoso_UI.ViewForRegistrations.RegisterAttributedViews(views);",
            text);
    }

    [Fact]
    public void GeneratedNamespaceOverride_InvalidValue_TakesPrecedenceOverMappingDiagnostics()
    {
        // The invalid override is the root cause: with it present, the
        // per-mapping reports stay suppressed (the same root-cause-first
        // precedence FLOWGEN002/014 follow) instead of double-reporting.
        var sources = "[assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace(\"1bad\")]\n\n" + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor<TestApp.ViewModels.BadViewModel>]
                public class BadView : Avalonia.Controls.UserControl { }
            }

            namespace TestApp.ViewModels
            {
                public class BadViewModel { }
            }
            """;

        var (runResult, diagnostics, _) = RunGenerator(
            sources, additionalReferences: new[] { FlowStubsReference });
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN016");
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN010");
        Assert.Empty(runResult.GeneratedSources);
    }

    [Fact]
    public void GeneratedNamespaceOverride_InvalidValue_TakesPrecedenceOverNoPlatformDiagnostic()
    {
        var sources = "[assembly: ElegantSeries.Flow.Routing.ViewForGeneratedNamespace(\"1bad\")]\n\n"
            + AttributeStub + BasicViews;

        var (runResult, diagnostics, _) = RunGenerator(sources);
        Assert.Contains(diagnostics, d => d.Id == "FLOWGEN016");
        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN014");
        Assert.Empty(runResult.GeneratedSources);
    }

    private const string NavigationViewModelStub = """
        namespace ElegantSeries.Flow.Navigation
        {
            public interface INavigationViewModel
            {
                INavigationService? Navigation { get; set; }
            }

            public interface INavigationService
            {
            }
        }
        """;
}
