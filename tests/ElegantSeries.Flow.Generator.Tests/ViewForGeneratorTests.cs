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
            "namespace TestCompilation.FlowGenerated",
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
            "namespace _2_cool.namespace_.FlowGenerated",
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
            "namespace TestCompilation.FlowGenerated",
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
            "namespace Foo._.FlowGenerated",
            dotGenerated.SourceText.ToString());

        // A whitespace-only assembly name falls back to the bare namespace.
        // (The compilation itself is invalid — CS8203 — so only the emitted
        // namespace line is pinned here, not the updated compilation.)
        var (blankRunResult, blankDiagnostics, _) = RunGenerator(sources, " ");
        Assert.Empty(blankDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var blankGenerated = Assert.Single(
            blankRunResult.GeneratedSources,
            s => s.HintName == "ViewForRegistrations.g.cs");
        var blankText = blankGenerated.SourceText.ToString();
        Assert.Contains("namespace ElegantSeries.Flow.Generated", blankText);
        Assert.DoesNotContain("namespace ElegantSeries.Flow.Generated.", blankText);
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
            "[assembly: global::ElegantSeries.Flow.Routing.GeneratedViewRegistrations]",
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
        var ownCall = "global::TestCompilation.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);";
        var providerCall = "global::Plugin.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);";
        Assert.Contains(ownCall, text);
        Assert.Contains(providerCall, text);
        Assert.True(
            text.IndexOf(ownCall, System.StringComparison.Ordinal) < text.IndexOf(providerCall, System.StringComparison.Ordinal),
            "The aggregation must call this assembly's own registrations before the providers'.");
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
        Assert.Contains("global::Plugin.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);", text);
        Assert.DoesNotContain("TestCompilation.FlowGenerated.ViewForRegistrations.RegisterAttributedViews", text);
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

                namespace Impostor.FlowGenerated
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

                namespace Cloaked.FlowGenerated
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

                namespace Generic.FlowGenerated
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
        // '.' 0x2E), but after it by sanitized type name ("Z.Mod.FlowGenerated" vs
        // "Z_Mod.FlowGenerated": '.' 0x2E < '_' 0x5F). The aggregation order is the raw
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
        var hyphenCall = "global::Z_Mod.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);";
        var dottedCall = "global::Z.Mod.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);";
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

                namespace Impostor.FlowGenerated
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
        var pluginCall = "global::Plugin.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);";
        var reportsCall = "global::Reports.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);";
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
                        global::Plugin.FlowGenerated.ViewForRegistrations.RegisterAttributedViews(views);
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
        // <assembly>.FlowGenerated namespace as the generated registrations without
        // colliding with them.
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

            namespace TestCompilation.FlowGenerated
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
    public void RootTypeNamedFlowGenerated_CollidesWithGeneratedNamespaceSegment()
    {
        // The accepted residual boundary of the FlowGenerated segment word:
        // a consumer type named exactly FlowGenerated inside the assembly
        // root namespace cannot coexist with the generated namespace
        // segment of the same name (CS0101, reported on the consumer's own
        // declaration). Renaming or relocating that one type is the remedy;
        // this pins the boundary so it is a recorded cost, not a surprise.
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

            namespace TestCompilation
            {
                public class FlowGenerated { }
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
        var generatedNamespace = string.Join(
            ".",
            assemblyName.Split('.').Select(segment => new string(
                segment.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray()))) + ".FlowGenerated";
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
