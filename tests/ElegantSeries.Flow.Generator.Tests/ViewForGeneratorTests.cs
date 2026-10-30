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
            [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
            public class HomeView : Avalonia.Controls.Control { }

            [ElegantSeries.Flow.Routing.ViewFor(
                typeof(TestApp.ViewModels.MenuViewModel),
                Lifetime = ElegantSeries.Flow.Routing.ViewModelLifetime.Singleton)]
            public class MenuView : Avalonia.Controls.Control { }

            [ElegantSeries.Flow.Routing.ViewFor(
                typeof(TestApp.ViewModels.DetailViewModel),
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
                public class HomeView : Avalonia.Controls.Control { }

                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
                public class OtherHomeView : Avalonia.Controls.Control { }
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
                [ElegantSeries.Flow.Routing.ViewFor(
                    typeof(TestApp.ViewModels.MenuViewModel),
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
                [ElegantSeries.Flow.Routing.ViewFor(
                    typeof(TestApp.ViewModels.HomeViewModel),
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
    public void NullViewModelType_ReportsFlowGen006()
    {
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                    [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.StructViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(
                    typeof(TestApp.ViewModels.StructViewModel),
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
                [ElegantSeries.Flow.Routing.ViewFor(
                    typeof(TestApp.ViewModels.StructViewModel),
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                    [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                    [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.HomeViewModel))]
                public class BrokenView : TypoBase { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(sources);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FLOWGEN011");
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
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
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
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

    [Fact]
    public void ViewModelNotImplementingInterface_ReportsFlowGen010()
    {
        // [ViewFor(typeof(PlainClass))] where PlainClass does not implement
        // INavigationViewModel must produce FLOWGEN010.
        var sources = AttributeStub + AvaloniaControlsStub + NavigationViewModelStub + AvaloniaLocatorStub + """
            namespace TestApp.Views
            {
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.PlainClass))]
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
                [ElegantSeries.Flow.Routing.ViewFor(typeof(TestApp.ViewModels.GoodViewModel))]
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
