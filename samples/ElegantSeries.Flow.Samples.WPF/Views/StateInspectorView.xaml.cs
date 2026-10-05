using System.Windows;
using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;
using ElegantSeries.Flow.WPF.Locating;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor]
public partial class StateInspectorView : ElegantSeries.Flow.WPF.Views.BaseView<StateInspectorViewModel>
{
    public StateInspectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // IViewLocator is platform-specific, so the shared ViewModel cannot
        // query it directly; the view reports the results back.
        var locator = App.Services.GetRequiredService<IViewLocator>();
        ViewModel?.ReportRegistrations(
            locator.IsRegistered<HomeViewModel>(),
            locator.IsRegistered<UnregisteredDemoViewModel>());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
