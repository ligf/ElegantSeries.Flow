using ElegantSeries.Flow.Core.Routing;
using ElegantSeries.Flow.Samples.Shared;

namespace ElegantSeries.Flow.Samples.WPF.Views;

[ViewFor]
public partial class EventsView : ElegantSeries.Flow.WPF.Views.BaseView<EventsDemoViewModel>
{
    public EventsView() => InitializeComponent();
}
