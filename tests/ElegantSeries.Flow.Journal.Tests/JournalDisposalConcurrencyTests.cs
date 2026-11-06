using System.Reflection;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// Disposal/enqueue concurrency for the journal decorator: the enqueue
/// boundary (an assigned sequence is always backed by a queued item, in
/// order) and the shared asynchronous disposal (concurrent callers join
/// one operation; the synchronous path never joins it), mirroring the
/// wrapped service's disposal rules.
/// </summary>
public sealed class JournalDisposalConcurrencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Stages an enqueue parked inside the boundary: the hook
    /// parks the FIRST enqueue after its sequence is assigned and before
    /// its item is written; later enqueues pass through.</summary>
    private sealed class ParkedEnqueue
    {
        private readonly TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public Task Parked => _parked.Task;

        public void Release() => _release.TrySetResult();

        public void Hook()
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _parked.TrySetResult();
                _release.Task.GetAwaiter().GetResult();
            }
        }
    }

    [Fact]
    public async Task DisposeAsync_EnqueueParkedInsideBoundary_CompletesAndTheParkedEntryLands()
    {
        var fake = new FakeNavigationService();
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());
        var gate = new ParkedEnqueue();
        decorator.EnqueueBoundaryHook = gate.Hook;

        // Enqueue A assigns its sequence, then parks before writing.
        var enqueueA = Task.Run(() => fake.RaiseViewModelReleased("MainRegion", new FakeViewModel()));
        await gate.Parked.WaitAsync(Timeout);

        // Disposal begins while A is still parked inside the boundary.
        var disposal = decorator.DisposeAsync().AsTask();

        // The disposal cannot complete — cannot even claim the service —
        // while the parked enqueue holds the boundary.
        Assert.False(disposal.IsCompleted);

        gate.Release();
        await enqueueA.WaitAsync(Timeout);
        await disposal.WaitAsync(Timeout);

        // The parked entry was written before the writer could complete,
        // so it lands through the drain: its sequence was not lost.
        var entries = await store.ReadEntriesAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(JournalEntryKind.Released, entry.Kind);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public async Task ConcurrentEnqueues_ItemsReachTheStoreInSequenceOrder()
    {
        var fake = new FakeNavigationService();
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());
        var gate = new ParkedEnqueue();
        decorator.EnqueueBoundaryHook = gate.Hook;

        // A assigns sequence 1 and parks before writing; B races it. B
        // must not be able to write sequence 2 ahead of A's item: the
        // consumer reads items one at a time and appends in sequence
        // order, so an overtaken item would stall it permanently.
        var enqueueA = Task.Run(() => fake.RaiseViewModelReleased("MainRegion", new FakeViewModel()));
        await gate.Parked.WaitAsync(Timeout);
        var enqueueB = Task.Run(() => fake.RaiseViewModelReleased("MainRegion", new FakeViewModel()));

        gate.Release();
        await Task.WhenAll(enqueueA, enqueueB).WaitAsync(Timeout);

        // Flush: returns once every queued item has landed.
        await decorator.ClearCacheAsync("MainRegion").AsTask().WaitAsync(Timeout);
        var entries = await store.ReadEntriesAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Sequence);
        Assert.Equal(2, entries[1].Sequence);

        await decorator.DisposeAsync().AsTask().WaitAsync(Timeout);
    }

    [Fact]
    public async Task ConcurrentDisposeAsync_SecondCallJoinsTheInFlightDisposal()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.DisposeAsyncHook = () =>
        {
            started.TrySetResult();
            return release.Task;
        };

        var first = decorator.DisposeAsync().AsTask();
        await started.Task.WaitAsync(Timeout);

        var second = decorator.DisposeAsync().AsTask();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, fake.DisposeAsyncCount);

        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public async Task ConcurrentDisposeAsync_JoinersObserveTheSameException()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.DisposeAsyncHook = () =>
        {
            started.TrySetResult();
            return release.Task;
        };
        fake.DisposeAsyncException = new InvalidOperationException("inner disposal failed");

        var first = decorator.DisposeAsync().AsTask();
        await started.Task.WaitAsync(Timeout);
        var second = decorator.DisposeAsync().AsTask();

        release.TrySetResult();
        var firstError = await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        var secondError = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Same(firstError, secondError);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_AfterCompletion_ReturnsImmediately()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());

        await decorator.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);

        var again = decorator.DisposeAsync();
        Assert.True(again.IsCompletedSuccessfully);
        Assert.Equal(1, fake.DisposeAsyncCount);

        decorator.Dispose();
        Assert.Equal(0, fake.DisposeCount);
    }

    [Fact]
    public async Task SyncDispose_DuringInFlightDisposeAsync_ReturnsImmediatelyAndDoesNotTakeOver()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.DisposeAsyncHook = () =>
        {
            started.TrySetResult();
            return release.Task;
        };

        var disposal = decorator.DisposeAsync().AsTask();
        await started.Task.WaitAsync(Timeout);

        // The synchronous path never joins the in-flight asynchronous
        // disposal: it returns at once and leaves the cleanup to it.
        var syncCall = Task.Run(() => decorator.Dispose());
        await syncCall.WaitAsync(Timeout);
        Assert.Equal(0, fake.DisposeCount);

        release.TrySetResult();
        await disposal.WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);
        Assert.Equal(0, fake.DisposeCount);
    }

    [Fact]
    public void SyncDispose_First_DisposeAsyncAfterwardsReturnsImmediately()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());

        decorator.Dispose();
        Assert.Equal(1, fake.DisposeCount);

        var afterwards = decorator.DisposeAsync();
        Assert.True(afterwards.IsCompleted);
        Assert.Equal(0, fake.DisposeAsyncCount);

        decorator.Dispose();
        Assert.Equal(1, fake.DisposeCount);
    }

    /// <summary>A one-shot parking gate for a test seam hook: the first
    /// (and every) invocation signals and then blocks until released.</summary>
    private sealed class ParkedGate
    {
        private readonly TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Parked => _parked.Task;

        public void Release() => _release.TrySetResult();

        public void Hook()
        {
            _parked.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task SyncDispose_ClaimingBeforeTheAsyncCoreStarts_AsyncCompletesWithoutDraining()
    {
        var fake = new FakeNavigationService();
        var store = new FirstAppendBlockingStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());

        // Entry 1 occupies the consumer inside the store; entry 2 sits
        // queued behind it.
        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());
        await store.FirstAppendStarted.WaitAsync(Timeout);
        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());

        // The async core parks before its claim.
        var prologue = new ParkedGate();
        decorator.DisposePrologueHook = prologue.Hook;
        var disposal = decorator.DisposeAsync().AsTask();
        await prologue.Parked.WaitAsync(Timeout);
        var joiner = decorator.DisposeAsync().AsTask();
        Assert.False(joiner.IsCompleted);

        // The synchronous path claims the service in that window and
        // owns the cleanup.
        decorator.Dispose();
        Assert.Equal(1, fake.DisposeCount);

        prologue.Release();
        await Task.WhenAll(disposal, joiner).WaitAsync(Timeout);
        Assert.Equal(0, fake.DisposeAsyncCount);

        // Entry 1 was already inside the store and lands; the still
        // queued entry 2 was abandoned by the synchronous cleanup, not
        // drained by the async operation.
        store.ReleaseFirstAppend();
        var consumer = (Task)typeof(JournalNavigationService)
            .GetField("_queueConsumer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(decorator)!;
        await consumer.WaitAsync(Timeout);
        var entry = Assert.Single(await store.Inner.ReadEntriesAsync());
        Assert.Equal(1, entry.Sequence);
    }

    [Fact]
    public async Task DisposeAsync_FromStoreAppendDuringDrain_ReturnsSilentlyAndTheDrainCompletes()
    {
        var fake = new FakeNavigationService();
        var store = new DrainCallbackStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());
        store.Service = decorator;

        // Entry 1 occupies the consumer inside the store; entry 2 waits
        // in the queue, so entry 2's append runs during the drain.
        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());
        await store.FirstAppendStarted.WaitAsync(Timeout);
        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());

        var disposal = decorator.DisposeAsync().AsTask();
        store.ReleaseFirstAppend();
        await disposal.WaitAsync(Timeout);

        // The callback inside entry 2's append neither started nor
        // joined a disposal: it returned silently, the entry landed,
        // and the outer disposal completed.
        Assert.Same(DrainCallbackStore.CompletedSentinel, store.CallbackOutcome);
        Assert.Equal(2, (await store.Inner.ReadEntriesAsync()).Count);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_FromStoreAppendBeforeAnyDisposal_FaultsAndRecordsNothing()
    {
        var fake = new FakeNavigationService();
        var store = new DisposeAsyncCallingStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());
        store.Service = decorator;

        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());

        // Settles once the queued entry (whose append makes the
        // callback) has landed.
        await decorator.ClearCacheAsync("MainRegion").AsTask().WaitAsync(Timeout);

        Assert.IsType<InvalidOperationException>(store.CallbackOutcome);
        Assert.Single(await store.Inner.ReadEntriesAsync());
        Assert.Equal(0, fake.DisposeCount);
        Assert.Equal(0, fake.DisposeAsyncCount);

        // The service was not disposed by the callback: a later,
        // ordinary call performs the disposal.
        await decorator.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }
}
