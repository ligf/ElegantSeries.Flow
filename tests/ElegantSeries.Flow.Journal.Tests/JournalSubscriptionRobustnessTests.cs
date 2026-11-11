using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Pins the subscription exception-isolation contract (mirroring the
/// Diagnostics package): a wrapped service whose Subscribe* methods or
/// subscription tokens throw must not leave the decorator half-built or
/// half-disposed.
/// </summary>
public class JournalSubscriptionRobustnessTests
{
    [Fact]
    public void Constructor_SubscribeThrows_RollsBackAcquiredSubscriptions()
    {
        // The decorator subscribes six taps in order (three recording
        // taps, then the republishing ones). Failing the fourth leaves
        // three acquired tokens that construction must release.
        var inner = new FakeNavigationService { ThrowOnSubscribeCall = 4 };
        var store = new InMemoryJournalStore();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new JournalNavigationService(inner, store, new JournalBuilder()));
        Assert.Equal("subscribe failed (test double)", ex.Message);
        Assert.Equal(0, inner.LiveSubscriptionCount);
    }

    [Fact]
    public async Task Constructor_SubscribeThrows_NoZombieRecording_SuccessorRecordsOnce()
    {
        var inner = new FakeNavigationService { ThrowOnSubscribeCall = 4 };
        var store = new InMemoryJournalStore();

        Assert.Throws<InvalidOperationException>(() =>
            new JournalNavigationService(inner, store, new JournalBuilder()));

        // Events raised after the failed construction reach no
        // recording tap: nothing is appended, now or later.
        inner.RaiseViewModelReleased("MainRegion", new FakeViewModel());
        inner.RaiseRegionCacheCleared("MainRegion");
        await Task.Delay(300);
        Assert.Empty(await store.ReadEntriesAsync());

        // A successor decorator over the same service and store
        // records each event exactly once — no zombie tap doubling
        // its entries.
        inner.ThrowOnSubscribeCall = 0;
        using var successor = new JournalNavigationService(inner, store, new JournalBuilder());
        inner.RaiseRegionCacheCleared("MainRegion");
        var entries = await JournalRecordingTests.WaitForEntriesAsync(store, 1);
        Assert.Single(entries);
        await Task.Delay(300);
        Assert.Single(await store.ReadEntriesAsync());
    }

    [Fact]
    public void Dispose_TokenDisposeThrows_InnerStillDisposed()
    {
        var inner = new FakeNavigationService { ThrowOnTokenDispose = true };
        var service = new JournalNavigationService(inner, new InMemoryJournalStore(), new JournalBuilder());

        // A throwing token must neither escape Dispose nor skip the
        // remaining tokens and the wrapped service's disposal.
        service.Dispose();

        Assert.Equal(1, inner.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_TokenDisposeThrows_InnerStillDisposed()
    {
        var inner = new FakeNavigationService { ThrowOnTokenDispose = true };
        var service = new JournalNavigationService(inner, new InMemoryJournalStore(), new JournalBuilder());

        await service.DisposeAsync();

        Assert.Equal(1, inner.DisposeAsyncCount);
    }
}
