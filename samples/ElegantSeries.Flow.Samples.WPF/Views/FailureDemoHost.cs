using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Samples.WPF.ViewModels;

namespace ElegantSeries.Flow.Samples.WPF.Views;

/// <summary>
/// A <see cref="ElegantSeries.Flow.WPF.Hosting.NavigationHost"/> that surfaces
/// view creation failures on the owning page instead of swallowing them
/// silently: the failed ViewModel type and error are written to
/// <see cref="ViewFailureViewModel.StatusMessage"/>.
/// </summary>
public sealed class FailureDemoHost : ElegantSeries.Flow.WPF.Hosting.NavigationHost
{
    protected override void OnViewCreationFailed(INavigationViewModel viewModel, Exception exception)
    {
        base.OnViewCreationFailed(viewModel, exception);
        if (DataContext is ViewFailureViewModel page)
            page.ReportViewCreationFailure(viewModel.GetType().Name, exception.Message);
    }
}
