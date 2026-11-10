namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// D2 (design doc <c>d2-journal-recorded-token-design.md</c>): the
/// recorded-instance guard of the §8 synthesis model is a one-shot
/// absorption token, not a lifetime mark. A call that records a
/// landing leaves the token only when the landing's settle
/// announcement was not already processed while the call was in
/// flight; the next announcement for the instance discharges it.
/// A previously recorded instance that lands again bypassing the
/// decorator is therefore synthesized like any other bypass landing.
/// </summary>
public sealed class JournalRecordedTokenTests
{
    private static (FakeNavigationService Fake, InMemoryJournalStore Store, JournalNavigationService Decorator) Build()
    {
        var fake = new FakeNavigationService();
        var store = new InMemoryJournalStore();
        return (fake, store, new JournalNavigationService(fake, store, new JournalBuilder()));
    }

    [Fact]
    public async Task LaggedAnnouncement_IsAbsorbedOnce_ThenBypassLandingSynthesizes()
    {
        var (fake, store, decorator) = Build();
        var vm = new FakeViewModel();
        fake.Current = vm;

        // The call records the landing; the fake announces only when
        // told, so no announcement has been processed yet and the
        // recording leaves the absorption token.
        Assert.True(await decorator.NavigateToAsync<FakeViewModel>());
        Assert.Single(await JournalRecordingTests.WaitForEntriesAsync(store, 1));

        // The landing's lagged announcement arrives: the token absorbs
        // it — no duplicate entry for the recorded landing.
        fake.RaiseRegionNavigated("MainRegion", vm);
        await Task.Delay(300);
        Assert.Single(await store.ReadEntriesAsync());

        // The same instance lands again, bypassing the decorator: the
        // token is spent, so this announcement synthesizes an entry.
        // With the former lifetime mark it was swallowed forever.
        fake.RaiseRegionNavigated("MainRegion", vm);
        var entries = await JournalRecordingTests.WaitForEntriesAsync(store, 2);
        Assert.Equal(2, entries.Count(e => e.Kind == JournalEntryKind.Navigated));
    }

    [Fact]
    public async Task PromptAnnouncement_LeavesNoToken_ThenBypassLandingSynthesizes()
    {
        var (fake, store, decorator) = Build();
        var vm = new FakeViewModel();
        fake.Current = vm;

        // The announcement is processed while the call is still in
        // flight (the hook runs inside the wrapped call): the in-flight
        // guard attributes it, and the recording must NOT leave a
        // token behind for it — a leftover token would absorb the
        // announcement of the instance's next landing instead.
        fake.NavigateHook = () => fake.RaiseRegionNavigated("MainRegion", vm);
        Assert.True(await decorator.NavigateToAsync<FakeViewModel>());
        Assert.Single(await JournalRecordingTests.WaitForEntriesAsync(store, 1));

        fake.NavigateHook = null;
        fake.RaiseRegionNavigated("MainRegion", vm);
        var entries = await JournalRecordingTests.WaitForEntriesAsync(store, 2);
        Assert.Equal(2, entries.Count(e => e.Kind == JournalEntryKind.Navigated));
    }

    [Fact]
    public async Task SynthesizedLanding_LeavesNoToken_NextBypassLandingSynthesizesAgain()
    {
        var (fake, store, decorator) = Build();
        var vm = new FakeViewModel();
        fake.Current = vm;

        // No call at all: the first announcement synthesizes an entry.
        fake.RaiseRegionNavigated("MainRegion", vm);
        Assert.Single(await JournalRecordingTests.WaitForEntriesAsync(store, 1));

        // The synthesis itself must not mark the instance: a second
        // bypass landing of the same instance synthesizes again. With
        // the former lifetime mark (which synthesis also set) it was
        // swallowed.
        fake.RaiseRegionNavigated("MainRegion", vm);
        var entries = await JournalRecordingTests.WaitForEntriesAsync(store, 2);
        Assert.Equal(2, entries.Count(e => e.Kind == JournalEntryKind.Navigated));
    }
}
