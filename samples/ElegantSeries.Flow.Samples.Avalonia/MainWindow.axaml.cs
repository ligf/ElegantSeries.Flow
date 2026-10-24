using Avalonia.Controls;
using ElegantSeries.Flow.Avalonia.Hosting;
using ElegantSeries.Flow.Avalonia.Locating;
using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Samples.Avalonia;

public partial class MainWindow : Window
{
    private readonly NavigationHost[] _hosts;

    public MainWindow()
    {
        InitializeComponent();

        _hosts = [SidebarHost, MainHost];
    }

    /// <summary>
    /// Attaches the navigation service and the view locator to both region
    /// hosts. Called by <see cref="MainWindowBootstrapper"/> before the
    /// startup navigation runs, so the first pages are shown.
    /// </summary>
    public void AttachNavigationHosts(INavigationService navigation, IViewLocator views)
    {
        // Both regions share the singleton navigation service but keep
        // independent stacks.
        foreach (var host in _hosts)
        {
            host.NavigationService = navigation;
            host.ViewLocator = views;
        }
    }

    public void ReportError(string message) => ErrorText.Text = message;

    protected override void OnClosed(EventArgs e)
    {
        // Best-effort: every host is disposed even if one of them throws,
        // mirroring the navigation service's own disposal semantics.
        List<Exception>? errors = null;
        foreach (var host in _hosts)
        {
            try
            {
                host.Dispose();
            }
            catch (Exception ex)
            {
                errors ??= [];
                errors.Add(ex);
            }
        }

        base.OnClosed(e);

        if (errors is { Count: 1 })
        {
            throw errors[0];
        }

        if (errors is { Count: > 1 })
        {
            throw new AggregateException("One or more navigation hosts failed to dispose.", errors);
        }
    }
}
