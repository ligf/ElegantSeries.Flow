using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Pins the <see cref="INavigationJournal.ClearAsync"/> linearization
/// contract: entries whose recording was decided before the call —
/// queued behind a busy consumer or waiting for their append turn —
/// are appended and then wiped as part of the operation; none of them
/// is present, or appears, after the call returns.
/// </summary>
public class JournalClearAsyncTests
{
    /// <summary>
    /// A store whose appends of <see cref="JournalEntryKind.Released"/>
    /// entries block until released; all other appends pass through.
    /// Blocking the consumer's Released append also parks the direct
    /// append of the Back entry behind it (append turns are sequenced),
    /// staging both in-flight recording paths at once.
    /// </summary>
    private sealed class ReleasedBlockingStore : INavigationJournalStore
    {
        private readonly InMemoryJournalStore _inner = new();
        private readonly TaskCompletionSource _releasedAppendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releasedAppendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReleasedAppendStarted => _releasedAppendStarted.Task;

        public void ReleaseReleasedAppend() => _releasedAppendRelease.TrySetResult();

        public async ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry.Kind == JournalEntryKind.Released)
            {
                _releasedAppendStarted.TrySetResult();
                await _releasedAppendRelease.Task;
            }
            await _inner.AppendAsync(entry, cancellationToken);
        }

        public ValueTask<IReadOnlyList<JournalEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default) =>
            _inner.ReadEntriesAsync(cancellationToken);

        public ValueTask SaveSnapshotAsync(JournalSnapshot snapshot, CancellationToken cancellationToken = default) =>
            _inner.SaveSnapshotAsync(snapshot, cancellationToken);

        public ValueTask<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadSnapshotAsync(cancellationToken);

        public ValueTask ClearAsync(CancellationToken cancellationToken = default) =>
            _inner.ClearAsync(cancellationToken);
    }

    /// <summary>
    /// A store whose snapshot saves block until released; appends and
    /// clears pass through.
    /// </summary>
    private sealed class SnapshotSaveBlockingStore : INavigationJournalStore
    {
        private readonly InMemoryJournalStore _inner = new();
        private readonly TaskCompletionSource _saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _saveRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SaveStarted => _saveStarted.Task;

        public void ReleaseSave() => _saveRelease.TrySetResult();

        public async ValueTask SaveSnapshotAsync(JournalSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _saveStarted.TrySetResult();
            await _saveRelease.Task;
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

    [Fact]
    public async Task ClearAsync_CaptureInFlight_SaveLandsBeforeWipe_SnapshotDoesNotResurrect()
    {
        var store = new SnapshotSaveBlockingStore();
        var provider = JournalRecordingTests.BuildProvider(j => j.UseStore(store));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());

        // The capture is in flight, blocked inside its snapshot save;
        // the clear must wait for it (facade serialization), then wipe
        // what the capture saved — the snapshot must not land after
        // the wipe and resurrect.
        var capture = journal.CaptureSnapshotAsync();
        await store.SaveStarted.WaitAsync(TimeSpan.FromSeconds(10));
        var clear = journal.ClearAsync();
        await Task.WhenAny(clear, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(clear.IsCompleted);

        store.ReleaseSave();
        Assert.NotNull(await capture.WaitAsync(TimeSpan.FromSeconds(10)));
        await clear.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(await store.LoadSnapshotAsync());
        Assert.Empty(await store.ReadEntriesAsync());
    }

    [Fact]
    public async Task ClearAsync_InFlightRecordingsLandAndAreWiped_NoneResurrect()
    {
        var store = new ReleasedBlockingStore();
        var provider = JournalRecordingTests.BuildProvider(j => j.UseStore(store));
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var journal = provider.GetRequiredService<INavigationJournal>();

        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>());

        // The GoBack's Released entry is picked up by the queue consumer
        // and blocks inside the store; its Back entry waits for its
        // append turn behind it. Both recordings were decided before
        // the clear below is called.
        var goBack = nav.GoBackAsync();
        await store.ReleasedAppendStarted.WaitAsync(TimeSpan.FromSeconds(10));

        var clear = journal.ClearAsync();
        // The clear cannot complete while a pre-clear recording is
        // still in flight: it must flush and drain first. (For the
        // fixed implementation this cannot complete at all while the
        // consumer is blocked; the bounded wait only gives a broken
        // implementation time to reveal itself.)
        await Task.WhenAny(clear, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(clear.IsCompleted);

        store.ReleaseReleasedAppend();
        await clear.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await goBack.WaitAsync(TimeSpan.FromSeconds(10)));

        // Everything recorded before the clear is gone — and the
        // in-flight appends did not resurrect anything after it.
        Assert.Empty(await store.ReadEntriesAsync());

        // The journal still records normally after the wipe.
        Assert.True(await nav.NavigateToAsync<ProfileViewModel>());
        var entries = await store.ReadEntriesAsync();
        Assert.Single(entries);
    }
}
