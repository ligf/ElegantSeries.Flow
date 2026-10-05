using System.Windows;
using ElegantSeries.Flow.Core.Extensions;
using ElegantSeries.Flow.Generated;
using ElegantSeries.Flow.WPF.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Samples.Shared;
using ElegantSeries.Flow.Samples.WPF.Views;

namespace ElegantSeries.Flow.Samples.WPF;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        // Core navigation.
        services.AddSingletonFlowNavigation();

        // ViewModel DI registrations that stay manual:
        // - Home/Detail carry [ViewFor(..., Lifetime = ViewOnly)]: the generator
        //   emits only the view mapping, so their DI registration stays here to
        //   demonstrate the decoupled style (custom DI setup: factory, decorators,
        //   keyed services, ...).
        // - FactoryDemoViewModel uses a custom view factory below (also manual DI).
        // - UnregisteredDemoViewModel is DI-only by design: no view is registered,
        //   so navigating to it exercises the host's view-creation-failure path.
        services.AddTransient<HomeViewModel>();
        services.AddTransient<DetailViewModel>();
        services.AddTransient<FactoryDemoViewModel>();
        services.AddTransient<UnregisteredDemoViewModel>();

        // AOT-safe view registration: the ElegantSeries.Flow source generator
        // turns each view's [ViewFor] attribute into the equivalent
        // IViewLocator.Register* call (no runtime reflection). Manual
        // registration remains available for special cases — see the custom
        // view factory below.
        services.AddFlowViews(views =>
        {
            views.RegisterAttributedViews();

            // Manual view registration: ManualDemoView carries no [ViewFor]
            // attribute. This is the traditional alternative to generated
            // registration — both mechanisms coexist; pick one per ViewModel.
            views.Register<ManualDemoView, ManualDemoViewModel>();

            // Custom view factory: the factory overload does not touch DI, so
            // the ViewModel needs its own DI registration (see above). Use a
            // factory when the view needs constructor arguments or other
            // custom construction logic.
            views.Register<FactoryDemoViewModel>(_ =>
            {
                var view = new FactoryDemoView();
                view.ApplyFactoryBadge();
                return view;
            });
        });

        Services = services.BuildServiceProvider();

        // Platform-specific wiring: the shared MenuViewModel only raises
        // OpenSecondWindowRequested; creating the actual Window is the host's job.
        Services.GetRequiredService<MenuViewModel>().OpenSecondWindowRequested +=
            () => new SecondWindow().Show();

        base.OnStartup(e);
    }
}
