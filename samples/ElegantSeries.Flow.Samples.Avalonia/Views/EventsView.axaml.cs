using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.Avalonia.Views;

[ViewFor]
public partial class EventsView : ElegantSeries.Flow.Avalonia.Views.BaseView<EventsDemoViewModel>
{
    public EventsView() => InitializeComponent();
}
