using System.Reflection;
using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// P2-1 (design doc <c>p2-1-journal-dispose-callback-design.md</c>):
/// disposal calls made from within a page lifecycle callback of the
/// wrapped service are refused by the decorator BEFORE it claims any
/// state — mirroring the wrapped service's own rule, read from its
/// <see cref="INavigationService.IsInLifecycleCallback"/> classification
/// on the caller's flow. Before the fix, the decorator claimed itself
/// first and only faulted when the disposal reached the wrapped
/// service, leaving the decorator disposed (silently recording nothing)
/// and the wrapped service alive and undisposed.
/// </summary>
public sealed class JournalCallbackDisposalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string CallbackDisposeMessage =
        "Dispose cannot be called from within a page lifecycle callback. " +
        "Call it outside the callback instead.";
    private const string CallbackDisposeAsyncMessage =
        "DisposeAsync cannot be called from within a page lifecycle callback. " +
        "Call it outside the callback instead.";

    [Fact]
    public async Task DisposeAttempts_FromPageCallback_AreRefusedBeforeClaim_AndDecoratorSurvives()
    {
        var provider = JournalRecordingTests.BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        Assert.True(await nav.NavigateToAsync<DisposeAttemptingViewModel>());
        var vm = Assert.IsType<DisposeAttemptingViewModel>(nav.GetCurrentViewModel());

        // The synchronous attempt inside the callback threw, claiming
        // nothing; the asynchronous attempt faulted its returned task
        // the same way. Both messages are the wrapped service's own.
        var syncError = Assert.IsType<InvalidOperationException>(vm.DisposeError);
        Assert.Equal(CallbackDisposeMessage, syncError.Message);
        var asyncError = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await vm.DisposeAsyncAttempt!);
        Assert.Equal(CallbackDisposeAsyncMessage, asyncError.Message);
        Assert.False(vm.Disposed);

        // The decorator is fully alive: navigation still works and is
        // still recorded.
        Assert.True(await nav.NavigateToAsync<SettingsViewModel>());
        var entries = await JournalRecordingTests.WaitForEntriesAsync(store, 2);
        Assert.Equal(
            [typeof(DisposeAttemptingViewModel).AssemblyQualifiedName, typeof(SettingsViewModel).AssemblyQualifiedName],
            entries.Where(e => e.Kind == JournalEntryKind.Navigated).Select(e => e.ViewModelType));

        // And a later, legal disposal runs to completion — the earlier
        // refusals did not silently consume it — disposing the wrapped
        // service and with it this page's scope.
        await ((IAsyncDisposable)nav).DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.True(vm.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_FromCallback_NoDisposalInFlight_FaultsAndDecoratorStaysAlive()
    {
        var fake = new FakeNavigationService { IsInLifecycleCallback = true };
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.DisposeAsync());
        Assert.Equal(CallbackDisposeAsyncMessage, error.Message);
        Assert.Equal(0, fake.DisposeAsyncCount);

        // Alive: once the caller is outside a callback, recording works
        // and a legal disposal disposes the wrapped service exactly once.
        fake.IsInLifecycleCallback = false;
        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());
        Assert.Single(await JournalRecordingTests.WaitForEntriesAsync(store, 1));

        await decorator.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_FromInnerScopeTeardown_ReturnsSilentlyAndDecoratorStaysAlive()
    {
        var fake = new FakeNavigationService { IsInScopeDisposal = true };
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());

        // The wrapped service returns silently for a DisposeAsync issued
        // from within one of its page scopes' teardowns; the decorator
        // mirrors that classification, read from
        // INavigationService.IsInScopeDisposal before claiming or joining
        // anything — the wrapped service's cleanup is inline-waiting for
        // that teardown, and the decorator's disposal would end by
        // awaiting the wrapped service's.
        var call = decorator.DisposeAsync();
        Assert.True(call.IsCompleted);
        await call;
        Assert.Equal(0, fake.DisposeAsyncCount);

        // Alive: once the caller is outside a scope teardown, recording
        // works and a legal disposal disposes the wrapped service once.
        fake.IsInScopeDisposal = false;
        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());
        Assert.Single(await JournalRecordingTests.WaitForEntriesAsync(store, 1));

        await decorator.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_FromCallback_StarterParkedBeforeClaim_StillFaults()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());

        // The first disposal's core is parked before its claim: a
        // disposal exists but cleanup has not begun, so the callback
        // rule faults exactly as if no disposal had been requested.
        var prologue = new ParkedGate();
        decorator.DisposePrologueHook = prologue.Hook;
        var disposal = decorator.DisposeAsync().AsTask();
        await prologue.Parked.WaitAsync(Timeout);

        fake.IsInLifecycleCallback = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.DisposeAsync());
        Assert.Equal(CallbackDisposeAsyncMessage, error.Message);

        prologue.Release();
        await disposal.WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_FromCallback_CleanupAlreadyBegun_ReturnsSilently()
    {
        var fake = new FakeNavigationService();
        var store = new FirstAppendBlockingStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());

        // Occupy the queue consumer inside the store's first append, so
        // the disposal's drain stays in flight after its claim.
        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());
        await store.FirstAppendStarted.WaitAsync(Timeout);

        var disposal = decorator.DisposeAsync().AsTask();
        await WaitForClaimAsync(decorator);

        // Cleanup has begun: the disposal is already waiting on work
        // that includes this callback's world, so the call returns
        // silently instead of faulting or joining.
        fake.IsInLifecycleCallback = true;
        var call = decorator.DisposeAsync();
        Assert.True(call.IsCompleted);
        Assert.False(call.IsFaulted);
        await call;

        store.ReleaseFirstAppend();
        await disposal.WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeAsyncCount);
    }

    [Fact]
    public void SyncDispose_FromCallback_ThrowsBeforeClaim_AndSilentlyAfterDisposal()
    {
        var fake = new FakeNavigationService { IsInLifecycleCallback = true };
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());

        var error = Assert.Throws<InvalidOperationException>(() => decorator.Dispose());
        Assert.Equal(CallbackDisposeMessage, error.Message);
        Assert.Equal(0, fake.DisposeCount);

        fake.IsInLifecycleCallback = false;
        decorator.Dispose();
        Assert.Equal(1, fake.DisposeCount);

        // Once disposed, a callback call returns silently — the wrapped
        // service's disposed-check-first rule.
        fake.IsInLifecycleCallback = true;
        decorator.Dispose();
        Assert.Equal(1, fake.DisposeCount);
    }

    [Fact]
    public void Query_ForwardsToWrappedService()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());

        Assert.False(decorator.IsInLifecycleCallback);
        fake.IsInLifecycleCallback = true;
        Assert.True(decorator.IsInLifecycleCallback);
    }

    private static async Task WaitForClaimAsync(JournalNavigationService decorator)
    {
        var field = typeof(JournalNavigationService)
            .GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var deadline = DateTime.UtcNow + Timeout;
        while ((int)field.GetValue(decorator)! == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The disposal did not claim the decorator in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>A one-shot parking gate for a test seam hook: invocation
    /// signals and then blocks until released.</summary>
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
}
