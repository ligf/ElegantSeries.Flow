using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Demonstrates manual view registration with <c>Singleton</c> ViewModel
/// lifetime: its view carries no <c>[ViewFor]</c> attribute and is registered
/// in App via <c>views.RegisterSingleton&lt;TView, TViewModel&gt;()</c>, which
/// registers both the view mapping and a singleton ViewModel in DI.
/// </summary>
public sealed partial class ManualSingletonDemoViewModel : BaseViewModel
{
}
