using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Demonstrates view creation failure handling: the embedded region navigates
/// to a ViewModel with no registered view. The host keeps showing the previous
/// content and reports the failure through <c>OnViewCreationFailed</c>, which
/// surfaces here as a status line instead of crashing the UI.
/// </summary>
public sealed partial class ViewFailureViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _statusMessage = "The region below is empty. Press the button to navigate it to a page with no registered view.";

    [RelayCommand]
    private Task NavigateToUnregisteredAsync()
        => NavigateToAsync<UnregisteredDemoViewModel>("FailureRegion");

    /// <summary>
    /// Called by <c>FailureDemoHost</c> when view creation fails. Runs on the
    /// UI thread (the host marshals navigation callbacks through its dispatcher).
    /// </summary>
    public void ReportViewCreationFailure(string viewModelName, string error)
        => StatusMessage = $"View creation failed for {viewModelName}: {error} — previous content kept.";
}
