using ElegantSeries.Flow.Mvvm;

namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Demonstrates manual view registration: its view carries no
/// <c>[ViewFor]</c> attribute and is registered explicitly in App via
/// <c>views.Register&lt;ManualDemoView, ManualDemoViewModel&gt;()</c>,
/// proving the framework stays fully compatible with traditional registration.
/// </summary>
public sealed partial class ManualDemoViewModel : BaseViewModel
{
}
