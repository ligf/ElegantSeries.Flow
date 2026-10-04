using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

public partial class ToolkitFreeView : ElegantSeries.Flow.Avalonia.Views.BaseView<ToolkitFreeDemoViewModel>
{
    public ToolkitFreeView() => InitializeComponent();

    private void OnIncrementClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => ViewModel?.Increment();

    private void OnBackClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            SampleHelpers.ObserveNavigation(ViewModel.LeaveAsync(), ReportError);
        }
    }

    private void ReportError(string message) => ErrorText.Text = message;
}
