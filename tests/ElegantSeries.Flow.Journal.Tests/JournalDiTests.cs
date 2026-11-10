using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Journal.Tests;

/// <summary>
/// DI-helper and decorator-unit tests: registration discipline mirrors
/// the diagnostics package; forwarding/disposal semantics are tested
/// against a fake inner service.
/// </summary>
public class JournalDiTests
{
    [Fact]
    public void JournalBeforeNavigation_Throws()
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddElegantSeriesFlowJournal());
        Assert.Contains("AddElegantSeriesFlow", error.Message);
    }

    [Fact]
    public void TransientNavigation_Throws()
    {
        // AddElegantSeriesFlow itself rejects Transient, so a transient
        // navigation service can only arrive via a manual registration;
        // the journal helper must reject that shape too.
        var services = new ServiceCollection();
        services.AddTransient<INavigationService>(_ => new FakeNavigationService());
        Assert.Throws<InvalidOperationException>(() => services.AddElegantSeriesFlowJournal());
    }

    [Fact]
    public void InvalidHistoryCap_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddElegantSeriesFlowJournal(j => j.Options.MaxHistoryEntries = 0));
    }

    [Fact]
    public void DoubleRegistration_SecondCallWithInvalidOptions_IsNoOp()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddElegantSeriesFlowJournal();
        // The keep-first check runs before options validation: a second
        // call is a no-op even when its own configuration is invalid.
        services.AddElegantSeriesFlowJournal(j => j.Options.MaxHistoryEntries = 0);
    }

    [Fact]
    public void DoubleRegistration_SecondCall_DoesNotInvokeConfigure()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddElegantSeriesFlowJournal();
        // The keep-first check runs before configuration: a redundant
        // second call must not execute the caller's configure delegate
        // into a builder that is then discarded.
        var invoked = false;
        services.AddElegantSeriesFlowJournal(_ => invoked = true);
        Assert.False(invoked);
    }

    [Fact]
    public async Task DoubleRegistration_WrapsOnce()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowJournal();
        services.AddElegantSeriesFlowJournal();
        using var provider = services.BuildServiceProvider();

        var nav = provider.GetRequiredService<INavigationService>();
        Assert.IsType<JournalNavigationService>(nav);
        Assert.True(await nav.NavigateToAsync<HomeViewModel>());

        var store = provider.GetRequiredService<INavigationJournalStore>();
        Assert.Single(await store.ReadEntriesAsync());
    }

    [Fact]
    public async Task PreRegisteredStore_IsUsedByDefault()
    {
        var store = new InMemoryJournalStore();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddSingleton<INavigationJournalStore>(store);
        services.AddElegantSeriesFlowJournal();
        using var provider = services.BuildServiceProvider();

        var nav = provider.GetRequiredService<INavigationService>();
        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.Single(await store.ReadEntriesAsync());
    }

    [Fact]
    public async Task ExplicitStore_ReplacesPreRegistered()
    {
        var preRegistered = new InMemoryJournalStore();
        var explicitStore = new InMemoryJournalStore();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddSingleton<INavigationJournalStore>(preRegistered);
        services.AddElegantSeriesFlowJournal(j => j.UseStore(explicitStore));
        using var provider = services.BuildServiceProvider();

        var nav = provider.GetRequiredService<INavigationService>();
        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.Single(await explicitStore.ReadEntriesAsync());
        Assert.Empty(await preRegistered.ReadEntriesAsync());
    }

    [Fact]
    public async Task ExplicitStore_RegisteredBeforeNavigation_WrapRemovesOnlyTheNavigationDescriptor()
    {
        var preRegistered = new InMemoryJournalStore();
        var explicitStore = new InMemoryJournalStore();
        var services = new ServiceCollection();
        // The store sits BEFORE the navigation descriptor: removing it
        // during journal registration must not shift the navigation
        // descriptor out from under the wrap.
        services.AddSingleton<INavigationJournalStore>(preRegistered);
        services.AddElegantSeriesFlow();
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowJournal(j => j.UseStore(explicitStore));
        using var provider = services.BuildServiceProvider();

        var nav = provider.GetRequiredService<INavigationService>();
        Assert.IsType<JournalNavigationService>(nav);
        Assert.Single(provider.GetServices<INavigationService>());
        Assert.NotNull(provider.GetRequiredService<HomeViewModel>());
        Assert.True(await nav.NavigateToAsync<HomeViewModel>());
        Assert.Single(await explicitStore.ReadEntriesAsync());
        Assert.Empty(await preRegistered.ReadEntriesAsync());
    }

    [Fact]
    public async Task ScopedNavigation_EachScopeHasOwnJournalView_SharedStore()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(ServiceLifetime.Scoped);
        services.AddTransient<HomeViewModel>();
        services.AddElegantSeriesFlowJournal();
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<INavigationJournalStore>();

        INavigationJournal journal1;
        using (var scope = provider.CreateScope())
        {
            journal1 = scope.ServiceProvider.GetRequiredService<INavigationJournal>();
            var nav = scope.ServiceProvider.GetRequiredService<INavigationService>();
            Assert.True(await nav.NavigateToAsync<HomeViewModel>("ScopeA"));
        }
        using (var scope = provider.CreateScope())
        {
            var journal2 = scope.ServiceProvider.GetRequiredService<INavigationJournal>();
            Assert.NotSame(journal1, journal2);
            var nav = scope.ServiceProvider.GetRequiredService<INavigationService>();
            Assert.True(await nav.NavigateToAsync<HomeViewModel>("ScopeB"));
        }

        Assert.Equal(2, (await store.ReadEntriesAsync()).Count);
    }

    [Fact]
    public async Task InstanceRegistration_InnerInstanceNotOwned_NotDisposedWithProvider()
    {
        var fake = new FakeNavigationService();
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService>(fake);
        services.AddElegantSeriesFlowJournal();
        var provider = services.BuildServiceProvider();

        var nav = provider.GetRequiredService<INavigationService>();
        Assert.IsType<JournalNavigationService>(nav);
        Assert.True(await nav.NavigateToAsync<HomeViewModel>());

        await provider.DisposeAsync();
        Assert.Equal(0, fake.DisposeCount);
        Assert.Equal(0, fake.DisposeAsyncCount);
    }

    // ── Decorator unit tests against the fake ───────────────────────

    [Fact]
    public async Task Decorator_ForwardsQueriesAndDisposalExactlyOnce()
    {
        var fake = new FakeNavigationService { CanGoBackValue = true };
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());

        Assert.True(decorator.CanGoBack());
        Assert.Same(fake.Stack, decorator.GetNavigationStack());

        await decorator.DisposeAsync();
        await decorator.DisposeAsync();
        decorator.Dispose();
        Assert.Equal(1, fake.DisposeAsyncCount);
        Assert.Equal(0, fake.DisposeCount);
    }

    [Fact]
    public async Task Decorator_DisposeAsyncDrainsQueuedReleasedEntries()
    {
        var fake = new FakeNavigationService();
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());

        fake.RaiseViewModelReleased("MainRegion", new FakeViewModel());
        await decorator.DisposeAsync();

        var entry = Assert.Single(await store.ReadEntriesAsync());
        Assert.Equal(JournalEntryKind.Released, entry.Kind);
        Assert.Equal(typeof(FakeViewModel).AssemblyQualifiedName, entry.ViewModelType);
    }

    [Fact]
    public async Task Decorator_DisposeAsyncRacingInFlightAppend_NavigationStillSucceeds()
    {
        // Regression: the append gate must survive disposal. Disposing
        // it while a call-site append is in flight faulted the (already
        // successful) navigation with ObjectDisposedException when the
        // append released the gate on its way out.
        var fake = new FakeNavigationService();
        var store = new FirstAppendBlockingStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());

        var navigation = decorator.NavigateToAsync<HomeViewModel>();
        await store.FirstAppendStarted; // the append is blocked inside the store
        await decorator.DisposeAsync(); // drains the empty queue, disposes the inner service
        store.ReleaseFirstAppend();

        Assert.True(await navigation);
        Assert.Single(await store.Inner.ReadEntriesAsync());
    }

    [Fact]
    public async Task Decorator_DeferredAnnouncementForRecordedPage_DoesNotDuplicateEntry()
    {
        // A page's settle announcement may be deferred until after the
        // producing call completed (a follow-up request was pending and
        // settled without displacing it). The deferred announcement
        // must not record the landing a second time.
        var home = new HomeViewModel();
        var fake = new FakeNavigationService { Current = home };
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());

        Assert.True(await decorator.NavigateToAsync<HomeViewModel>());
        fake.RaiseRegionNavigated("MainRegion", home);

        Assert.Single(await store.ReadEntriesAsync());
    }

    [Fact]
    public async Task Decorator_GoBackLandingAnnouncement_BindsLandedPage()
    {
        // Every GoBack landing is announced while its own call is in
        // flight; the announcement must still fill the landed page's
        // binding (the in-flight guard decides ENTRIES, not bindings).
        var landed = new FakeViewModel();
        var fake = new FakeNavigationService
        {
            Current = landed,
            CurrentRetentionValue = NavigationRetention.KeepAlive,
        };
        fake.Stack.Add(landed);
        var store = new InMemoryJournalStore();
        var decorator = new JournalNavigationService(fake, store, new JournalBuilder());
        var journal = new NavigationJournal(decorator, store);
        fake.GoBackHook = () =>
        {
            fake.RaiseRegionNavigated("MainRegion", landed);
            return Task.CompletedTask;
        };

        Assert.True(await decorator.GoBackAsync());

        var entry = Assert.Single(await store.ReadEntriesAsync());
        Assert.Equal(JournalEntryKind.Back, entry.Kind);
        var snapshot = await journal.CaptureSnapshotAsync();
        var page = Assert.Single(Assert.Single(snapshot.Regions).Pages);
        Assert.Equal(NavigationRetention.KeepAlive, page.Retention);
    }

    [Fact]
    public async Task Decorator_BypassLandingDuringInFlightCall_KeepsItsOwnBinding()
    {
        // A bypass landing of the same type as an in-flight call loses
        // its ENTRY to the in-flight guard, but its binding must still
        // be captured from its own effective parameter — the in-flight
        // call's post-call binding must not bury it under a
        // retention-only binding.
        var bypassPage = new DetailViewModel();
        var fake = new FakeNavigationService
        {
            CurrentRetentionValue = NavigationRetention.KeepAlive,
            ParameterOwner = bypassPage,
            ParameterValue = new DetailParam(2),
        };
        var store = new InMemoryJournalStore();
        var builder = new JournalBuilder();
        builder.RegisterParameter<DetailParam>(
            p => "id=" + p.Id,
            text => new DetailParam(int.Parse(text["id=".Length..])));
        var decorator = new JournalNavigationService(fake, store, builder);
        var journal = new NavigationJournal(decorator, store);
        fake.NavigateHook = () =>
        {
            fake.Current = bypassPage;
            fake.RaiseRegionNavigated("MainRegion", bypassPage);
        };

        Assert.True(await decorator.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(1)));

        fake.Stack.Add(bypassPage);
        var snapshot = await journal.CaptureSnapshotAsync();
        var page = Assert.Single(Assert.Single(snapshot.Regions).Pages);
        Assert.Equal("id=2", page.Parameter);
        Assert.Equal(NavigationRetention.KeepAlive, page.Retention);
    }

    [Fact]
    public async Task Decorator_NullRegion_ThrowsArgumentNullExceptionLikeTheWrappedService()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await decorator.NavigateToAsync<HomeViewModel>((string)null!));
    }

    [Fact]
    public async Task Decorator_InvalidRegion_FailsBeforeTouchingTheSerializer()
    {
        // Region validation mirrors the wrapped service and runs
        // first: an invalid call must not invoke (or report) the
        // parameter serializer at all.
        var fake = new FakeNavigationService();
        var builder = new JournalBuilder();
        builder.RegisterParameter<DetailParam>(
            _ => throw new InvalidOperationException("serializer boom"),
            text => new DetailParam(int.Parse(text["id=".Length..])));
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), builder);
        var errors = new List<Exception>();
        decorator.SubscribeSubscriberError(errors.Add);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await decorator.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(1), (string)null!));
        Assert.Empty(errors);
    }

    [Fact]
    public void Decorator_EventSubscribersAreIsolated_ThrowingOneReportsAndOthersStillRun()
    {
        var fake = new FakeNavigationService();
        var decorator = new JournalNavigationService(fake, new InMemoryJournalStore(), new JournalBuilder());
        var received = new List<string>();
        var errors = new List<Exception>();
        decorator.SubscribeRegionNavigated((_, _) => throw new InvalidOperationException("handler boom"));
        decorator.SubscribeRegionNavigated((region, _) => received.Add(region));
        decorator.SubscribeSubscriberError(errors.Add);

        fake.RaiseRegionNavigated("MainRegion", new FakeViewModel());

        Assert.Equal(["MainRegion"], received);
        var error = Assert.Single(errors);
        Assert.Equal("handler boom", error.Message);
    }
}
