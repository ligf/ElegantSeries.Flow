using System.Diagnostics.Metrics;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Diagnostics.Tests;

/// <summary>
/// Contract tests for <see cref="NavigationDiagnostics"/>: every test maps
/// to a section of the contract in
/// <c>files/diagnostics-design.md</c>. Measurements are captured with a
/// <see cref="MeterListener"/> that is started before the observer is
/// created, so recording is synchronous and assertions need no waiting.
/// Tests in this class run sequentially, which keeps the shared meter
/// name from cross-counting between tests.
/// </summary>
public class NavigationDiagnosticsTests
{
    [Fact]
    public async Task CompletedNavigations_AreCounted_VetoedNavigation_IsNot()
    {
        using var collector = new MeasurementCollector();
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        using var diagnostics = new NavigationDiagnostics(nav);

        Assert.True(await nav.NavigateToAsync<PageAViewModel>());
        Assert.True(await nav.NavigateToAsync<GuardedViewModel>());
        Assert.Equal(2, collector.Sum("navigation.completed", "MainRegion"));

        // Leaving GuardedViewModel is vetoed by its guard: no
        // RegionNavigated is raised, so the counter must not move.
        Assert.False(await nav.NavigateToAsync<PageBViewModel>());
        Assert.Equal(2, collector.Sum("navigation.completed", "MainRegion"));
    }

    [Fact]
    public async Task ReleasedViewModel_IsCounted()
    {
        using var collector = new MeasurementCollector();
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        using var diagnostics = new NavigationDiagnostics(nav);

        await nav.NavigateToAsync<PageAViewModel>();
        Assert.Equal(0, collector.Sum("viewmodel.released", "MainRegion"));

        // A page covered by a Push keeps its scope while it stays on the
        // stack — the release happens when a page actually leaves the
        // stack. Popping PageB (default Destroy retention) releases it.
        await nav.NavigateToAsync<PageBViewModel>();
        Assert.Equal(0, collector.Sum("viewmodel.released", "MainRegion"));
        Assert.True(await nav.GoBackAsync());
        Assert.Equal(1, collector.Sum("viewmodel.released", "MainRegion"));
    }

    [Fact]
    public async Task CacheClears_AreCountedPerRegion()
    {
        using var collector = new MeasurementCollector();
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        using var diagnostics = new NavigationDiagnostics(nav);

        // A KeepAlive page enters the cache only when it leaves the
        // stack: popping PageB (KeepAlive) moves it into the cache
        // instead of releasing it. Clearing that region then counts.
        await nav.NavigateToAsync<PageAViewModel>(retention: NavigationRetention.KeepAlive);
        await nav.NavigateToAsync<PageBViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.True(await nav.GoBackAsync());
        nav.ClearCache("MainRegion");
        Assert.Equal(1, collector.Sum("cache.cleared", "MainRegion"));
        Assert.Equal(0, collector.Sum("cache.cleared", "SecondRegion"));

        // Cache a page in each region, then clear all: one event per
        // region that actually held a cached entry.
        await nav.NavigateToAsync<PageBViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.True(await nav.GoBackAsync());
        await nav.NavigateToAsync<PageAViewModel>("SecondRegion", retention: NavigationRetention.KeepAlive);
        await nav.NavigateToAsync<PageBViewModel>("SecondRegion", retention: NavigationRetention.KeepAlive);
        Assert.True(await nav.GoBackAsync("SecondRegion"));
        nav.ClearAllCache();
        Assert.Equal(2, collector.Sum("cache.cleared", "MainRegion"));
        Assert.Equal(1, collector.Sum("cache.cleared", "SecondRegion"));
    }

    [Fact]
    public async Task ThrowingSubscriber_IsCounted_WithExceptionTypeTag()
    {
        using var collector = new MeasurementCollector();
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        using var diagnostics = new NavigationDiagnostics(nav);

        using var throwing = nav.SubscribeRegionNavigated(
            (_, _) => throw new InvalidOperationException("Test subscriber failure."));

        await nav.NavigateToAsync<PageAViewModel>();

        Assert.Equal(1, collector.SumWhere(
            "subscriber.errors",
            tags => tags.TryGetValue("error.type", out var type) &&
                    (string?)type == typeof(InvalidOperationException).FullName));
    }

    [Fact]
    public async Task Dispose_StopsCounting_AndIsIdempotent()
    {
        using var collector = new MeasurementCollector();
        var stub = new StubNavigationService();
        var diagnostics = new NavigationDiagnostics(stub);

        stub.RaiseRegionNavigated("MainRegion", new PageAViewModel());
        Assert.Equal(1, stub.NavigatedInvocations);
        Assert.Equal(1, collector.Sum("navigation.completed", "MainRegion"));

        diagnostics.Dispose();
        diagnostics.Dispose();

        // Every subscription was disposed exactly once — the second
        // Dispose is a no-op. The unsubscription is asserted at the
        // stub, not only through the meter: Dispose also disposes the
        // meter, whose counters silently drop late Adds, so a meter-sum
        // assertion alone cannot distinguish "unsubscribed" from
        // "still subscribed but counting into a dead meter".
        Assert.Equal(1, stub.NavigatedSubscriptionDisposals);
        Assert.Equal(1, stub.ReleasedSubscriptionDisposals);
        Assert.Equal(1, stub.CacheClearedSubscriptionDisposals);
        Assert.Equal(1, stub.SubscriberErrorSubscriptionDisposals);

        // Later events never reach the handlers at all.
        stub.RaiseRegionNavigated("MainRegion", new PageAViewModel());
        Assert.Equal(1, stub.NavigatedInvocations);
        Assert.Equal(1, collector.Sum("navigation.completed", "MainRegion"));

        // The navigation service itself was NOT disposed by the
        // observer: it still navigates successfully (a disposed
        // service would return false here).
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        var realDiagnostics = new NavigationDiagnostics(nav);
        realDiagnostics.Dispose();
        Assert.True(await nav.NavigateToAsync<PageBViewModel>());
    }

    [Fact]
    public async Task ServiceDisposal_CacheClearedForHeldRegion_IsCounted()
    {
        // Core raises RegionCacheCleared at service disposal for every
        // region that still held stack, cache or pending entries — even
        // one whose cache is empty. Those shutdown firings are counted.
        using var collector = new MeasurementCollector();
        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        using var diagnostics = new NavigationDiagnostics(nav);

        await nav.NavigateToAsync<PageAViewModel>();
        Assert.Equal(0, collector.Sum("cache.cleared", "MainRegion"));

        ((IDisposable)nav).Dispose();
        Assert.Equal(1, collector.Sum("cache.cleared", "MainRegion"));
    }

    [Fact]
    public void Handlers_InvokedAfterDispose_DoNotThrowAndDoNotCount()
    {
        // Core dispatches events from a snapshot, so a handler can be
        // invoked after its subscription was disposed (and after this
        // observer's Dispose). The contract: counted or dropped, never
        // an exception.
        //
        // Two stagings, because a meter-sum assertion on its own
        // cannot pin the "not counted" half: Dispose disposes the
        // meter too, and a disposed meter's counters silently drop
        // late Adds, so this test would stay green even if the
        // handlers' disposal guard were deleted.
        // (1) The stub fires each handler from inside its
        //     subscription's Dispose — the snapshot race landing in
        //     the window after the disposed flag is set but before
        //     the meter is disposed. That window is the one place the
        //     guard is the only thing preventing a count, and a count
        //     there WOULD be recorded (the meter is still alive), so
        //     the zero sums below fail if the guard regresses.
        // (2) The captured handlers are then invoked directly, after
        //     Dispose has fully returned, and must not throw.
        using var collector = new MeasurementCollector();
        var stub = new StubNavigationService(raiseOnUnsubscribe: true);
        var diagnostics = new NavigationDiagnostics(stub);
        var navigated = stub.NavigatedHandler!;
        var released = stub.ReleasedHandler!;
        var cacheCleared = stub.CacheClearedHandler!;
        var subscriberError = stub.SubscriberErrorHandler!;

        diagnostics.Dispose();

        // (1) Every handler really was invoked during Dispose, and
        // none of the invocations counted.
        Assert.Equal(1, stub.NavigatedInvocations);
        Assert.Equal(1, stub.ReleasedInvocations);
        Assert.Equal(1, stub.CacheClearedInvocations);
        Assert.Equal(1, stub.SubscriberErrorInvocations);
        Assert.Equal(0, collector.Sum("navigation.completed", "MainRegion"));
        Assert.Equal(0, collector.Sum("viewmodel.released", "MainRegion"));
        Assert.Equal(0, collector.Sum("cache.cleared", "MainRegion"));
        Assert.Equal(0, collector.SumWhere("subscriber.errors", _ => true));

        // (2) Direct invocation after Dispose: no throw, from any of
        // the four handlers.
        navigated("MainRegion", new PageAViewModel());
        released("MainRegion", new PageAViewModel());
        cacheCleared("MainRegion");
        subscriberError(new InvalidOperationException("Late dispatch."));
    }

    [Fact]
    public async Task ThrowingMeasurementListener_DoesNotFaultNavigation_OrReportSubscriberError()
    {
        // A MeterListener's measurement callback runs synchronously
        // inside Counter.Add and may throw. The observer's handlers
        // must swallow that: the navigation must not fault, and the
        // recording failure must not escape into the service's
        // subscriber-error channel (where it would be counted as a
        // navigation error — contaminating subscriber.errors itself).
        using var collector = new MeasurementCollector();
        using var throwingListener = new MeterListener();
        throwingListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == NavigationDiagnostics.MeterName &&
                instrument.Name == "navigation.completed")
                listener.EnableMeasurementEvents(instrument);
        };
        throwingListener.SetMeasurementEventCallback<long>(
            (_, _, _, _) => throw new InvalidOperationException("Listener failure."));
        throwingListener.Start();

        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        using var diagnostics = new NavigationDiagnostics(nav);

        var reportedErrors = 0;
        using var errorSubscription = nav.SubscribeSubscriberError(_ => reportedErrors++);

        Assert.True(await nav.NavigateToAsync<PageAViewModel>());
        Assert.Equal(0, reportedErrors);
        Assert.Equal(0, collector.SumWhere("subscriber.errors", _ => true));
    }

    [Fact]
    public void Constructor_ThrowingInstrumentPublishedListener_Propagates()
    {
        // Publishing an instrument can throw (a listener's
        // InstrumentPublished callback). The constructor must not
        // swallow it. (That the rollback region also disposes the
        // half-created meter is not observable from outside — no
        // public API enumerates live meters — so this test pins the
        // propagation only; the disposal is by construction.)
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == NavigationDiagnostics.MeterName)
                throw new InvalidOperationException("InstrumentPublished failure.");
        };
        listener.Start();

        var provider = BuildProvider();
        using var _p = provider;
        var nav = provider.GetRequiredService<INavigationService>();
        Assert.Throws<InvalidOperationException>(() => new NavigationDiagnostics(nav));
    }

    [Fact]
    public void Constructor_NullService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NavigationDiagnostics(null!));
    }

    [Fact]
    public async Task ServiceCollectionRegistration_ResolvesSingleton_AndCounts()
    {
        using var collector = new MeasurementCollector();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddElegantSeriesFlowDiagnostics();
        services.AddTransient<PageAViewModel>();
        var provider = services.BuildServiceProvider();
        using var _p = provider;

        var diagnostics = provider.GetRequiredService<NavigationDiagnostics>();
        Assert.Same(diagnostics, provider.GetRequiredService<NavigationDiagnostics>());

        var nav = provider.GetRequiredService<INavigationService>();
        await nav.NavigateToAsync<PageAViewModel>();
        Assert.Equal(1, collector.Sum("navigation.completed", "MainRegion"));
    }

    [Fact]
    public async Task ServiceCollectionRegistration_ScopedNavigationService_ObserverIsScopedToo()
    {
        // Core supports a scoped navigation service (one per window
        // scope). The observer must be scoped as well: a singleton
        // observer would fail scope validation — or, worse, silently
        // capture the root scope's unused instance and count nothing.
        using var collector = new MeasurementCollector();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(ServiceLifetime.Scoped);
        services.AddElegantSeriesFlowDiagnostics();
        services.AddTransient<PageAViewModel>();
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using var _p = provider;

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<NavigationDiagnostics>());

        using var scope = provider.CreateScope();
        var nav = scope.ServiceProvider.GetRequiredService<INavigationService>();
        var diagnostics = scope.ServiceProvider.GetRequiredService<NavigationDiagnostics>();
        Assert.Same(diagnostics, scope.ServiceProvider.GetRequiredService<NavigationDiagnostics>());

        using var otherScope = provider.CreateScope();
        Assert.NotSame(diagnostics, otherScope.ServiceProvider.GetRequiredService<NavigationDiagnostics>());

        await nav.NavigateToAsync<PageAViewModel>();
        Assert.Equal(1, collector.Sum("navigation.completed", "MainRegion"));
    }

    [Fact]
    public async Task ContainerOwnedObserver_DoesNotObserveShutdownFirings()
    {
        // The container creates the navigation service before the
        // observer that depends on it and disposes in reverse order —
        // so on the container-owned path the observer is already
        // unsubscribed when the service raises its shutdown
        // RegionCacheCleared, and the firing is not counted (the
        // manually-managed path does count it; see
        // ServiceDisposal_CacheClearedForHeldRegion_IsCounted).
        using var collector = new MeasurementCollector();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddElegantSeriesFlowDiagnostics();
        services.AddTransient<PageAViewModel>();
        var provider = services.BuildServiceProvider();

        var nav = provider.GetRequiredService<INavigationService>();
        _ = provider.GetRequiredService<NavigationDiagnostics>();
        await nav.NavigateToAsync<PageAViewModel>();

        // The observer WAS counting while alive — so the zero below
        // is the disposal order at work, not a dead subscription.
        Assert.Equal(1, collector.Sum("navigation.completed", "MainRegion"));

        provider.Dispose();
        Assert.Equal(0, collector.Sum("cache.cleared", "MainRegion"));
    }

    [Fact]
    public async Task ServiceCollectionRegistration_KeyedNavigationService_IsIgnoredByLifetimeMirror()
    {
        // Keyed registrations are invisible to the unkeyed resolution
        // the observer performs: an unrelated keyed transient must not
        // become the mirrored lifetime (it would fail the helper with
        // a bogus transient rejection).
        using var collector = new MeasurementCollector();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddKeyedTransient<INavigationService, NavigationService>("audit");
        services.AddElegantSeriesFlowDiagnostics();
        services.AddTransient<PageAViewModel>();
        var provider = services.BuildServiceProvider();
        using var _p = provider;

        var nav = provider.GetRequiredService<INavigationService>();
        _ = provider.GetRequiredService<NavigationDiagnostics>();
        await nav.NavigateToAsync<PageAViewModel>();
        Assert.Equal(1, collector.Sum("navigation.completed", "MainRegion"));
    }

    [Fact]
    public void ServiceCollectionRegistration_OnlyKeyedNavigationService_Throws()
    {
        // Only a keyed registration exists: there is no unkeyed
        // INavigationService for the observer to consume, so the
        // helper fails fast like the no-registration case.
        var services = new ServiceCollection();
        services.AddKeyedSingleton<INavigationService, NavigationService>("main");
        Assert.Throws<InvalidOperationException>(() => services.AddElegantSeriesFlowDiagnostics());
    }

    [Fact]
    public void ServiceCollectionRegistration_ManualSingletonNavigationService_MirrorsSingleton()
    {
        // A manually registered navigation service is mirrored like
        // the Core-registered one, and calling the helper twice stays
        // a no-op (TryAdd) rather than stacking registrations.
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddElegantSeriesFlowDiagnostics();
        services.AddElegantSeriesFlowDiagnostics();

        var descriptors = services.Where(d => d.ServiceType == typeof(NavigationDiagnostics)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(ServiceLifetime.Singleton, descriptors[0].Lifetime);
    }

    [Fact]
    public void ServiceCollectionRegistration_MultipleUnkeyedRegistrations_MirrorsTheLastOne()
    {
        // Unkeyed single-service resolution — the kind the observer's
        // constructor performs — uses the LAST unkeyed registration,
        // so the mirror must take the last unkeyed descriptor's
        // lifetime, not the first one it encounters.
        var scopedLast = new ServiceCollection();
        scopedLast.AddSingleton<INavigationService, NavigationService>();
        scopedLast.AddScoped<INavigationService, NavigationService>();
        scopedLast.AddElegantSeriesFlowDiagnostics();
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(scopedLast, d => d.ServiceType == typeof(NavigationDiagnostics)).Lifetime);

        var singletonLast = new ServiceCollection();
        singletonLast.AddScoped<INavigationService, NavigationService>();
        singletonLast.AddSingleton<INavigationService, NavigationService>();
        singletonLast.AddElegantSeriesFlowDiagnostics();
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(singletonLast, d => d.ServiceType == typeof(NavigationDiagnostics)).Lifetime);
    }

    [Fact]
    public void ServiceCollectionRegistration_KeyedRegistrationAfterUnkeyed_IsNotMirrored()
    {
        // The scan skips keyed descriptors even when one comes last:
        // a keyed transient registered after the unkeyed singleton
        // must not become the mirrored lifetime (it would fail the
        // helper with a bogus transient rejection).
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddKeyedTransient<INavigationService, NavigationService>("audit");
        services.AddElegantSeriesFlowDiagnostics();
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, d => d.ServiceType == typeof(NavigationDiagnostics)).Lifetime);
    }

    [Fact]
    public void ServiceCollectionRegistration_LastUnkeyedRegistrationTransient_Throws()
    {
        // The transient rejection also follows the last unkeyed
        // descriptor: an earlier singleton registration does not
        // rescue a collection whose effective registration is
        // transient.
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddTransient<INavigationService, NavigationService>();
        Assert.Throws<InvalidOperationException>(() => services.AddElegantSeriesFlowDiagnostics());
    }

    [Fact]
    public void ServiceCollectionRegistration_WithoutNavigationService_Throws()
    {
        // The helper must mirror the navigation service's lifetime, so
        // it refuses to guess: registering the observer first is a
        // startup error, not a silently-wrong singleton.
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddElegantSeriesFlowDiagnostics());
    }

    [Fact]
    public void ServiceCollectionRegistration_TransientNavigationService_Throws()
    {
        // A transient navigation service gives every resolution its own
        // isolated instance; an observer over it would count nothing.
        var services = new ServiceCollection();
        services.AddTransient<INavigationService, NavigationService>();
        Assert.Throws<InvalidOperationException>(() => services.AddElegantSeriesFlowDiagnostics());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<PageAViewModel>();
        services.AddTransient<PageBViewModel>();
        services.AddTransient<GuardedViewModel>();
        return services.BuildServiceProvider();
    }

    private sealed class PageAViewModel : NavigationViewModelBase
    {
    }

    private sealed class PageBViewModel : NavigationViewModelBase
    {
    }

    private sealed class GuardedViewModel : NavigationViewModelBase, INavigationGuard
    {
        public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    /// <summary>
    /// <see cref="INavigationService"/> double that captures the
    /// handlers the observer subscribes and lets the test invoke them
    /// on demand. With <c>raiseOnUnsubscribe</c>, each subscription's
    /// <see cref="IDisposable.Dispose"/> invokes its handler one last
    /// time — staging a snapshot dispatch that lands while the
    /// observer is mid-Dispose. Only the subscription surface is
    /// functional; every other member throws
    /// <see cref="NotImplementedException"/>.
    /// </summary>
    private sealed class StubNavigationService : INavigationService
    {
        private readonly bool _raiseOnUnsubscribe;

        public StubNavigationService(bool raiseOnUnsubscribe = false) =>
            _raiseOnUnsubscribe = raiseOnUnsubscribe;

        public Action<string, INavigationViewModel>? NavigatedHandler { get; private set; }
        public Action<string, INavigationViewModel>? ReleasedHandler { get; private set; }
        public Action<string>? CacheClearedHandler { get; private set; }
        public Action<Exception>? SubscriberErrorHandler { get; private set; }

        public int NavigatedInvocations { get; private set; }
        public int ReleasedInvocations { get; private set; }
        public int CacheClearedInvocations { get; private set; }
        public int SubscriberErrorInvocations { get; private set; }

        public int NavigatedSubscriptionDisposals { get; private set; }
        public int ReleasedSubscriptionDisposals { get; private set; }
        public int CacheClearedSubscriptionDisposals { get; private set; }
        public int SubscriberErrorSubscriptionDisposals { get; private set; }

        public void RaiseRegionNavigated(string regionName, INavigationViewModel viewModel)
        {
            if (NavigatedHandler is { } handler)
            {
                NavigatedInvocations++;
                handler(regionName, viewModel);
            }
        }

        public IDisposable SubscribeRegionNavigated(Action<string, INavigationViewModel> handler)
        {
            NavigatedHandler = handler;
            return new StubSubscription(
                onDisposeAttempt: () => NavigatedSubscriptionDisposals++,
                unsubscribe: () =>
                {
                    if (_raiseOnUnsubscribe)
                    {
                        NavigatedInvocations++;
                        handler("MainRegion", new PageAViewModel());
                    }

                    NavigatedHandler = null;
                });
        }

        public IDisposable SubscribeViewModelReleased(Action<string, INavigationViewModel> handler)
        {
            ReleasedHandler = handler;
            return new StubSubscription(
                onDisposeAttempt: () => ReleasedSubscriptionDisposals++,
                unsubscribe: () =>
                {
                    if (_raiseOnUnsubscribe)
                    {
                        ReleasedInvocations++;
                        handler("MainRegion", new PageAViewModel());
                    }

                    ReleasedHandler = null;
                });
        }

        public IDisposable SubscribeRegionCacheCleared(Action<string> handler)
        {
            CacheClearedHandler = handler;
            return new StubSubscription(
                onDisposeAttempt: () => CacheClearedSubscriptionDisposals++,
                unsubscribe: () =>
                {
                    if (_raiseOnUnsubscribe)
                    {
                        CacheClearedInvocations++;
                        handler("MainRegion");
                    }

                    CacheClearedHandler = null;
                });
        }

        public IDisposable SubscribeSubscriberError(Action<Exception> handler)
        {
            SubscriberErrorHandler = handler;
            return new StubSubscription(
                onDisposeAttempt: () => SubscriberErrorSubscriptionDisposals++,
                unsubscribe: () =>
                {
                    if (_raiseOnUnsubscribe)
                    {
                        SubscriberErrorInvocations++;
                        handler(new InvalidOperationException("Snapshot dispatch during disposal."));
                    }

                    SubscriberErrorHandler = null;
                });
        }

        /// <summary>
        /// Subscription token whose disposal ATTEMPTS are all counted
        /// (so a caller disposing a token twice is observable), while
        /// the unsubscribe action itself runs exactly once.
        /// </summary>
        private sealed class StubSubscription(Action onDisposeAttempt, Action unsubscribe) : IDisposable
        {
            private Action? _unsubscribe = unsubscribe;

            public void Dispose()
            {
                onDisposeAttempt();
                Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
            }
        }

        // The observer subscribes exclusively through the Subscribe*
        // methods; the events themselves are never used.
        public event Action<string, INavigationViewModel>? RegionNavigated { add { } remove { } }
        public event Action<string, INavigationViewModel>? ViewModelReleased { add { } remove { } }
        public event Action<string>? RegionCacheCleared { add { } remove { } }
        public event Action<Exception>? SubscriberError { add { } remove { } }

        public bool CanGoBack(string regionName = "MainRegion") => throw new NotImplementedException();
        public INavigationViewModel? GetCurrentViewModel(string regionName = "MainRegion") => throw new NotImplementedException();
        public bool IsActive<TViewModel>(string regionName = "MainRegion") where TViewModel : INavigationViewModel => throw new NotImplementedException();
        public NavigationRetention? GetCurrentRetention(string regionName = "MainRegion") => throw new NotImplementedException();
        public object? GetCurrentParameter(INavigationViewModel viewModel, string regionName = "MainRegion") => throw new NotImplementedException();
        public IReadOnlyList<INavigationViewModel> GetNavigationStack(string regionName = "MainRegion") => throw new NotImplementedException();
        public Task<bool> NavigateToAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(string regionName = "MainRegion", NavigationOperation operation = NavigationOperation.Push, NavigationRetention retention = NavigationRetention.Destroy, bool refreshIfActive = false, CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public Task<bool> NavigateToAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(TParam parameter, string regionName = "MainRegion", NavigationOperation operation = NavigationOperation.Push, NavigationRetention retention = NavigationRetention.Destroy, bool refreshIfActive = false, CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public Task<bool> GoBackAsync(string regionName = "MainRegion", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> GoBackToAsync<TViewModel>(string regionName = "MainRegion", CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public Task<bool> GoBackToAsync<TViewModel, TParam>(TParam parameter, string regionName = "MainRegion", CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public Task<bool> SetRootAsync<TViewModel>(string regionName = "MainRegion", NavigationRetention retention = NavigationRetention.Destroy, CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public Task<bool> SetRootAsync<TViewModel, TParam>(TParam parameter, string regionName = "MainRegion", NavigationRetention retention = NavigationRetention.Destroy, CancellationToken cancellationToken = default)
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public int Remove<TViewModel>(string regionName = "MainRegion")
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public int Remove<TViewModel, TParam>(TParam parameter, string regionName = "MainRegion")
            where TViewModel : INavigationViewModel
            => throw new NotImplementedException();
        public void ClearCache(string regionName) => throw new NotImplementedException();
        public void ClearAllCache() => throw new NotImplementedException();
        public ValueTask ClearCacheAsync(string regionName) => throw new NotImplementedException();
        public ValueTask ClearAllCacheAsync() => throw new NotImplementedException();
    }

    /// <summary>
    /// Records every measurement published on the diagnostics meter.
    /// Started before the observer under test is created so instrument
    /// publication is observed and enabled from the beginning.
    /// </summary>
    private sealed class MeasurementCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> _measurements = [];
        private readonly Lock _lock = new();

        public MeasurementCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == NavigationDiagnostics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var copy = new Dictionary<string, object?>();
                foreach (var tag in tags)
                    copy[tag.Key] = tag.Value;
                lock (_lock)
                    _measurements.Add((instrument.Name, value, copy));
            });
            _listener.Start();
        }

        public long Sum(string instrument, string region) => SumWhere(
            instrument,
            tags => tags.TryGetValue("region", out var value) && (string?)value == region);

        public long SumWhere(string instrument, Func<IReadOnlyDictionary<string, object?>, bool> tagFilter)
        {
            lock (_lock)
            {
                long total = 0;
                foreach (var (name, value, tags) in _measurements)
                {
                    if (name == instrument && tagFilter(tags))
                        total += value;
                }

                return total;
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
