using System.Windows;
using System.Windows.Threading;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// Windows-only tests for <see cref="WpfDispatcher"/>.
/// </summary>
public sealed class WpfDispatcherTests
{
    [Fact]
    public void NullDispatcher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new WpfDispatcher(null!));
    }

    [Fact]
    public void Post_NullAction_ThrowsArgumentNullException()
    {
        StaHelper.Run(() =>
        {
            var dispatcher = new WpfDispatcher(Dispatcher.CurrentDispatcher);

            Assert.Throws<ArgumentNullException>(() => dispatcher.Post(null!));
        });
    }

    [Fact]
    public void CheckAccess_OnDispatcherThread_ReturnsTrue()
    {
        StaHelper.Run(() =>
        {
            var dispatcher = new WpfDispatcher(Dispatcher.CurrentDispatcher);

            Assert.True(dispatcher.CheckAccess());
        });
    }

    [Fact]
    public void DefaultConstructor_FallsBackToCurrentThreadDispatcher()
    {
        StaHelper.Run(() =>
        {
            var dispatcher = new WpfDispatcher();

            // No Application in tests: falls back to the calling thread's dispatcher.
            Assert.True(dispatcher.CheckAccess());
        });
    }

    [Fact]
    public void Post_ExecutesActionOnDispatcher()
    {
        StaHelper.Run(() =>
        {
            var wpfDispatcher = Dispatcher.CurrentDispatcher;
            var dispatcher = new WpfDispatcher(wpfDispatcher);

            bool ran = false;
            dispatcher.Post(() => ran = true);

            // Pump the queue: the posted action runs before the background pump.
            wpfDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.True(ran);
        });
    }
}
