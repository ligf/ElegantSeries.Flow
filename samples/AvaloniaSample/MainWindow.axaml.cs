using Avalonia.Controls;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        MainHost.NavigationService = App.Services.GetRequiredService<INavigationService>();
        MainHost.ViewLocator = App.Services.GetRequiredService<IViewLocator>();
    }

    protected override void OnClosed(EventArgs e)
    {
        MainHost.Dispose(); // unsubscribes from the navigation service
        base.OnClosed(e);
    }
}
