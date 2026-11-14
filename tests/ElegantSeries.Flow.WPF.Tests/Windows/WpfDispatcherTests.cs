using System.Windows;
using System.Windows.Threading;
using ElegantSeries.Flow.WPF.Threading;

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
    public void SelectDispatcher_PrefersConstructingThreadOverApplication()
    {
        var threadDispatcher = StaHelper.Run(() => Dispatcher.CurrentDispatcher);
        var applicationDispatcher = StaHelper.Run(() => Dispatcher.CurrentDispatcher);
        Assert.NotSame(threadDispatcher, applicationDispatcher);

        // A host constructed on a secondary UI thread must marshal to
        // that thread, not to the application's main dispatcher.
        var selected = WpfDispatcher.SelectDispatcher(
            threadDispatcher, applicationDispatcher, () => Dispatcher.CurrentDispatcher);

        Assert.Same(threadDispatcher, selected);
    }

    [Fact]
    public void SelectDispatcher_FallsBackInOrder_AndLazily()
    {
        var applicationDispatcher = StaHelper.Run(() => Dispatcher.CurrentDispatcher);
        var fallbackDispatcher = StaHelper.Run(() => Dispatcher.CurrentDispatcher);
        int fallbackCalls = 0;
        Dispatcher Fallback()
        {
            fallbackCalls++;
            return fallbackDispatcher;
        }

        Assert.Same(applicationDispatcher, WpfDispatcher.SelectDispatcher(null, applicationDispatcher, Fallback));
        Assert.Equal(0, fallbackCalls);

        Assert.Same(fallbackDispatcher, WpfDispatcher.SelectDispatcher(null, null, Fallback));
        Assert.Equal(1, fallbackCalls);
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
