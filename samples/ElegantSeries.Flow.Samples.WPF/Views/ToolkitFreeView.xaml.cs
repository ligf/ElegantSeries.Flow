using System.Windows;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

public partial class ToolkitFreeView : ElegantSeries.Flow.WPF.Views.BaseView<ToolkitFreeDemoViewModel>
{
    public ToolkitFreeView() => InitializeComponent();

    private void OnIncrementClicked(object sender, RoutedEventArgs e)
        => ViewModel?.Increment();

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            SampleHelpers.ObserveNavigation(ViewModel.LeaveAsync(), ReportError);
        }
    }

    private void ReportError(string message) =>
        MessageBox.Show(message, "ToolkitFree", MessageBoxButton.OK, MessageBoxImage.Warning);
}
