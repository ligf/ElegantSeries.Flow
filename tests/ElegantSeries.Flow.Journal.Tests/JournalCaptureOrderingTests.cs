using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Contract test for capture/save ordering: overlapping
/// <see cref="INavigationJournal.CaptureSnapshotAsync"/> calls must not
/// leave an older picture as the stored snapshot. The core capture is
/// serialized inside the decorator, so this pins the journal-level
/// half: the save is part of the same serialized unit.
/// </summary>
public class JournalCaptureOrderingTests
{
    [Fact]
    public async Task OverlappingCaptures_StoredSnapshotIsTheLaterCapture()
    {
        var inner = new FakeNavigationService();
        var home = new FakeViewModel();
        inner.AllStacksValue = new Dictionary<string, IReadOnlyList<INavigationViewModel>>
        {
            ["MainRegion"] = [home],
        };
        var store = new GatedStore();
        var decorator = new JournalNavigationService(inner, store, new JournalBuilder());
        var journal = new NavigationJournal(decorator, store);

        var captureA = journal.CaptureSnapshotAsync();
        // A has captured the one-page state and is parked inside its save.
        await store.FirstSaveEntered.Task;

        // The world moves on: a second page appears.
        inner.AllStacksValue = new Dictionary<string, IReadOnlyList<INavigationViewModel>>
        {
            ["MainRegion"] = [home, new FakeViewModel()],
        };
        var captureB = journal.CaptureSnapshotAsync();

        // Give B every chance to run ahead of A's save. It must not be
        // able to: without the journal-level serialization B captures
        // and saves immediately, and A's older picture then overwrites
        // B's when A's save finally lands.
        await Task.Delay(200);
        Assert.False(captureB.IsCompleted);

        store.ReleaseFirstSave.TrySetResult();
        var snapshotA = await captureA;
        var snapshotB = await captureB;
        Assert.Single(Assert.Single(snapshotA.Regions).Pages);
        Assert.Equal(2, Assert.Single(snapshotB.Regions).Pages.Count);

        var stored = await journal.LoadSnapshotAsync();
        Assert.NotNull(stored);
        Assert.Equal(2, Assert.Single(stored!.Regions).Pages.Count);
        Assert.Equal(snapshotB.CapturedUtc, stored.CapturedUtc);
    }

    /// <summary>
    /// Store double whose FIRST snapshot save parks until released, so
    /// a test can hold one capture inside its save while a second
    /// capture is attempted. Everything delegates to an in-memory store.
    /// </summary>
    private sealed class GatedStore : INavigationJournalStore
    {
        private readonly InMemoryJournalStore _inner = new();
        private int _saveCount;

        public TaskCompletionSource FirstSaveEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask SaveSnapshotAsync(
            JournalSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saveCount) == 1)
            {
                FirstSaveEntered.TrySetResult();
                await ReleaseFirstSave.Task;
            }

            await _inner.SaveSnapshotAsync(snapshot, cancellationToken);
        }

        public ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default) =>
            _inner.AppendAsync(entry, cancellationToken);

        public ValueTask<IReadOnlyList<JournalEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default) =>
            _inner.ReadEntriesAsync(cancellationToken);

        public ValueTask<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadSnapshotAsync(cancellationToken);

        public ValueTask ClearAsync(CancellationToken cancellationToken = default) =>
            _inner.ClearAsync(cancellationToken);
    }
}
