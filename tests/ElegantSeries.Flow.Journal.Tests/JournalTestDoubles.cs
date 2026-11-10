using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Journal.Tests;

// ── ViewModels & parameters ─────────────────────────────────────────

public sealed class HomeViewModel : NavigationViewModelBase
{
}

public sealed class SettingsViewModel : NavigationViewModelBase
{
}

public sealed class ProfileViewModel : NavigationViewModelBase
{
}

public sealed class DetailViewModel : NavigationViewModelBase, INavigationAware<DetailParam>
{
    public DetailParam? Received { get; private set; }

    public void OnNavigatedTo(DetailParam parameter) => Received = parameter;

    public void OnNavigatedFrom()
    {
    }
}

public sealed class GuardedViewModel : NavigationViewModelBase, INavigationGuard
{
    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken) => Task.FromResult(false);
}

public sealed class MutableDetailViewModel : NavigationViewModelBase, INavigationAware<MutableParam>
{
    public MutableParam? Received { get; private set; }

    public void OnNavigatedTo(MutableParam parameter) => Received = parameter;

    public void OnNavigatedFrom()
    {
    }
}

public sealed record DetailParam(int Id);

public sealed record UnregisteredParam(int Id);

public sealed class MutableParam
{
    public string Value { get; set; } = "";
}

/// <summary>Registers a chained navigation to Detail(5) from its own
/// OnNavigatedTo callback, through the attached Navigation property —
/// which always references the wrapped service itself, bypassing the
/// journal decorator.</summary>
public sealed class CallbackViewModel : NavigationViewModelBase, INavigationAware
{
    public void OnNavigatedTo(object? parameter)
    {
        _ = Navigation!.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(5));
    }

    public void OnNavigatedFrom()
    {
    }
}

/// <summary>Registers a chained navigation to Detail(7) from its own
/// OnNavigatedTo callback, through the constructor-injected navigation
/// service — which resolves to the journal decorator.</summary>
public sealed class InjectedCallbackViewModel : NavigationViewModelBase, INavigationAware
{
    public InjectedCallbackViewModel(INavigationService navigation)
    {
        InjectedService = navigation;
    }

    public INavigationService InjectedService { get; }

    public void OnNavigatedTo(object? parameter)
    {
        _ = InjectedService.NavigateToAsync<DetailViewModel, DetailParam>(new DetailParam(7));
    }

    public void OnNavigatedFrom()
    {
    }
}

/// <summary>Attempts to dispose the constructor-injected navigation
/// service (which resolves to the journal decorator) from inside its
/// own OnNavigatedTo callback — synchronously and asynchronously —
/// capturing how each attempt ended instead of letting it fault the
/// navigation. Also reports its own disposal, so a test can tell when
/// the wrapped service's disposal actually reached this page's
/// scope.</summary>
public sealed class DisposeAttemptingViewModel : NavigationViewModelBase, INavigationAware, IDisposable
{
    private readonly INavigationService _injected;

    public DisposeAttemptingViewModel(INavigationService navigation)
    {
        _injected = navigation;
    }

    public Exception? DisposeError { get; private set; }

    public Task? DisposeAsyncAttempt { get; private set; }

    public bool Disposed { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        try
        {
            ((IDisposable)_injected).Dispose();
        }
        catch (Exception ex)
        {
            DisposeError = ex;
        }

        DisposeAsyncAttempt = ((IAsyncDisposable)_injected).DisposeAsync().AsTask();
    }

    public void OnNavigatedFrom()
    {
    }

    public void Dispose() => Disposed = true;
}

/// <summary>A Detail page that claims any DetailParam through the
/// target vote, so GoBackTo with a NEW parameter reactivates it and
/// overwrites its stored parameter.</summary>
public sealed class VoteDetailViewModel
    : NavigationViewModelBase, INavigationAware<DetailParam>, INavigationTargetAware
{
    public DetailParam? Received { get; private set; }

    public void OnNavigatedTo(DetailParam parameter) => Received = parameter;

    public void OnNavigatedFrom()
    {
    }

    public bool IsNavigationTarget(object? parameter) => parameter is DetailParam;
}

// ── Stores ──────────────────────────────────────────────────────────

/// <summary>A store whose append always fails, for recording-isolation
/// tests. Reads behave like an empty store.</summary>
public sealed class ThrowingStore : INavigationJournalStore
{
    public ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("store is down");

    public ValueTask<IReadOnlyList<JournalEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default) =>
        new(new List<JournalEntry>());

    public ValueTask SaveSnapshotAsync(JournalSnapshot snapshot, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask<JournalSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
        new((JournalSnapshot?)null);

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

/// <summary>
/// An in-memory store whose CacheCleared appends block until released,
/// so a test can observe whether an async clear's completion really
/// waits for the queued entry to land.
/// </summary>
public sealed class BlockableStore : INavigationJournalStore
{
    private readonly InMemoryJournalStore _inner = new();
    private readonly TaskCompletionSource _cacheAppendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cacheAppendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task CacheAppendStarted => _cacheAppendStarted.Task;

    public void ReleaseCacheAppend() => _cacheAppendRelease.TrySetResult();

    public async ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Kind == JournalEntryKind.CacheCleared)
        {
            _cacheAppendStarted.TrySetResult();
            await _cacheAppendRelease.Task;
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
/// An in-memory store whose FIRST append blocks until released, so a
/// test can hold a call-site append in flight inside the store while it
/// disposes the decorator concurrently.
/// </summary>
public sealed class FirstAppendBlockingStore : INavigationJournalStore
{
    private readonly InMemoryJournalStore _inner = new();
    private readonly TaskCompletionSource _firstAppendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstAppendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _appendCount;

    public InMemoryJournalStore Inner => _inner;

    public Task FirstAppendStarted => _firstAppendStarted.Task;

    public void ReleaseFirstAppend() => _firstAppendRelease.TrySetResult();

    public async ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _appendCount) == 1)
        {
            _firstAppendStarted.TrySetResult();
            await _firstAppendRelease.Task;
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
/// An in-memory store whose FIRST append calls back into the journal
/// service's DisposeAsync (a settable target), records how that call
/// ended, and then appends normally — user code tearing the service
/// down from inside a store callback.
/// </summary>
public sealed class DisposeAsyncCallingStore : INavigationJournalStore
{
    private readonly InMemoryJournalStore _inner = new();
    private int _appendCount;

    public JournalNavigationService? Service { get; set; }

    /// <summary>Null until the first append's callback ran; then either
    /// <see cref="CompletedSentinel"/> or the observed exception.</summary>
    public object? CallbackOutcome { get; private set; }

    public static readonly object CompletedSentinel = new();

    public InMemoryJournalStore Inner => _inner;

    public async ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _appendCount) == 1 && Service is not null)
        {
            try
            {
                await Service.DisposeAsync();
                CallbackOutcome = CompletedSentinel;
            }
            catch (Exception ex)
            {
                CallbackOutcome = ex;
            }
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
/// An in-memory store whose FIRST append blocks until released, and
/// whose SECOND append first calls back into the journal service's
/// DisposeAsync and records how that call ended — for staging a store
/// callback that runs on the queue consumer during a disposal drain.
/// </summary>
public sealed class DrainCallbackStore : INavigationJournalStore
{
    private readonly InMemoryJournalStore _inner = new();
    private readonly TaskCompletionSource _firstAppendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstAppendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _appendCount;

    public JournalNavigationService? Service { get; set; }

    public object? CallbackOutcome { get; private set; }

    public static readonly object CompletedSentinel = new();

    public InMemoryJournalStore Inner => _inner;

    public Task FirstAppendStarted => _firstAppendStarted.Task;

    public void ReleaseFirstAppend() => _firstAppendRelease.TrySetResult();

    public async ValueTask AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _appendCount);
        if (count == 1)
        {
            _firstAppendStarted.TrySetResult();
            await _firstAppendRelease.Task;
        }
        else if (count == 2 && Service is not null)
        {
            try
            {
                await Service.DisposeAsync();
                CallbackOutcome = CompletedSentinel;
            }
            catch (Exception ex)
            {
                CallbackOutcome = ex;
            }
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

// ── Fake navigation service ─────────────────────────────────────────

/// <summary>
/// A configurable <see cref="INavigationService"/> double for decorator
/// unit tests. Event channels (event accessors and Subscribe*) share one
/// handler list per event, mirroring the real service's delivery.
/// </summary>
public sealed class FakeNavigationService : INavigationService, IDisposable, IAsyncDisposable
{
    private readonly List<Action<string, INavigationViewModel>> _navigatedHandlers = [];
    private readonly List<Action<string, INavigationViewModel>> _releasedHandlers = [];
    private readonly List<Action<string>> _cacheClearedHandlers = [];
    private readonly List<Action<Exception>> _errorHandlers = [];

    public bool NavigateResult { get; set; } = true;
    public INavigationViewModel? Current { get; set; }
    public List<INavigationViewModel> Stack { get; } = [];
    public bool CanGoBackValue { get; set; }
    public NavigationRetention CurrentRetentionValue { get; set; } = NavigationRetention.Destroy;

    /// <summary>The instance <see cref="GetCurrentParameter"/> reports
    /// <see cref="ParameterValue"/> for (all other instances: null).</summary>
    public INavigationViewModel? ParameterOwner { get; set; }
    public object? ParameterValue { get; set; }

    /// <summary>Invoked synchronously inside both NavigateToAsync
    /// overloads, while the call is still in flight.</summary>
    public Action? NavigateHook { get; set; }

    /// <summary>Invoked inside GoBackAsync, while the call is still in
    /// flight.</summary>
    public Func<Task>? GoBackHook { get; set; }
    public int DisposeCount { get; private set; }
    public int DisposeAsyncCount { get; private set; }
    public List<string> Calls { get; } = [];

    /// <summary>When nonzero, the Nth Subscribe* call (counted across
    /// all four Subscribe* methods) throws instead of subscribing.</summary>
    public int ThrowOnSubscribeCall { get; set; }

    /// <summary>When set, subscription tokens throw from Dispose
    /// instead of unsubscribing.</summary>
    public bool ThrowOnTokenDispose { get; set; }

    private int _subscribeCalls;

    /// <summary>Handlers currently held through the Subscribe*
    /// methods and the event accessors combined.</summary>
    public int LiveSubscriptionCount =>
        _navigatedHandlers.Count + _releasedHandlers.Count +
        _cacheClearedHandlers.Count + _errorHandlers.Count;

    public void RaiseRegionNavigated(string region, INavigationViewModel viewModel)
    {
        foreach (var handler in _navigatedHandlers.ToList())
            handler(region, viewModel);
    }

    public void RaiseViewModelReleased(string region, INavigationViewModel viewModel)
    {
        foreach (var handler in _releasedHandlers.ToList())
            handler(region, viewModel);
    }

    public void RaiseRegionCacheCleared(string region)
    {
        foreach (var handler in _cacheClearedHandlers.ToList())
            handler(region);
    }

    public event Action<string, INavigationViewModel>? RegionNavigated
    {
        add { if (value is not null) _navigatedHandlers.Add(value); }
        remove { if (value is not null) _navigatedHandlers.Remove(value); }
    }

    public event Action<string, INavigationViewModel>? ViewModelReleased
    {
        add { if (value is not null) _releasedHandlers.Add(value); }
        remove { if (value is not null) _releasedHandlers.Remove(value); }
    }

    public event Action<string>? RegionCacheCleared
    {
        add { if (value is not null) _cacheClearedHandlers.Add(value); }
        remove { if (value is not null) _cacheClearedHandlers.Remove(value); }
    }

    public event Action<Exception>? SubscriberError
    {
        add { if (value is not null) _errorHandlers.Add(value); }
        remove { if (value is not null) _errorHandlers.Remove(value); }
    }

    public IDisposable SubscribeRegionNavigated(Action<string, INavigationViewModel> handler)
    {
        ThrowIfSubscribeArmed();
        _navigatedHandlers.Add(handler);
        return NewToken(() => _navigatedHandlers.Remove(handler));
    }

    public IDisposable SubscribeViewModelReleased(Action<string, INavigationViewModel> handler)
    {
        ThrowIfSubscribeArmed();
        _releasedHandlers.Add(handler);
        return NewToken(() => _releasedHandlers.Remove(handler));
    }

    public IDisposable SubscribeRegionCacheCleared(Action<string> handler)
    {
        ThrowIfSubscribeArmed();
        _cacheClearedHandlers.Add(handler);
        return NewToken(() => _cacheClearedHandlers.Remove(handler));
    }

    public IDisposable SubscribeSubscriberError(Action<Exception> handler)
    {
        ThrowIfSubscribeArmed();
        _errorHandlers.Add(handler);
        return NewToken(() => _errorHandlers.Remove(handler));
    }

    private void ThrowIfSubscribeArmed()
    {
        if (++_subscribeCalls == ThrowOnSubscribeCall)
            throw new InvalidOperationException("subscribe failed (test double)");
    }

    private Token NewToken(Action unsubscribe) => new(() =>
    {
        if (ThrowOnTokenDispose)
            throw new InvalidOperationException("token dispose failed (test double)");
        unsubscribe();
    });

    /// <summary>The value <see cref="IsInLifecycleCallback"/> reports;
    /// settable so decorator tests can simulate a caller inside one of
    /// the wrapped service's page lifecycle callbacks.</summary>
    public bool IsInLifecycleCallback { get; set; }

    /// <summary>The value <see cref="IsInScopeDisposal"/> reports;
    /// settable so decorator tests can simulate a caller inside one of
    /// the wrapped service's page scope teardowns.</summary>
    public bool IsInScopeDisposal { get; set; }

    public bool CanGoBack(string regionName = "MainRegion") => CanGoBackValue;

    public INavigationViewModel? GetCurrentViewModel(string regionName = "MainRegion") => Current;

    public bool IsActive<TViewModel>(string regionName = "MainRegion")
        where TViewModel : INavigationViewModel => Current is TViewModel;

    public NavigationRetention? GetCurrentRetention(string regionName = "MainRegion") => CurrentRetentionValue;

    public object? GetCurrentParameter(INavigationViewModel viewModel, string regionName = "MainRegion") =>
        ReferenceEquals(viewModel, ParameterOwner) ? ParameterValue : null;

    public IReadOnlyList<INavigationViewModel> GetNavigationStack(string regionName = "MainRegion")
    {
        GetNavigationStackCallCount++;
        return Stack;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<INavigationViewModel>>? _allStacksValue;

    /// <summary>The value <see cref="GetAllNavigationStacks"/> reports.
    /// Settable so capture tests can stage regions the decorator has
    /// never seen; when unset, the atomic read derives from
    /// <see cref="Stack"/> as the MainRegion stack — a real service's two
    /// stack queries always agree with each other, so the fake's do
    /// too.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<INavigationViewModel>> AllStacksValue
    {
        get => _allStacksValue ??
            (Stack.Count == 0
                ? new Dictionary<string, IReadOnlyList<INavigationViewModel>>()
                : new Dictionary<string, IReadOnlyList<INavigationViewModel>> { ["MainRegion"] = Stack });
        set => _allStacksValue = value;
    }

    public int GetNavigationStackCallCount { get; private set; }
    public int GetAllNavigationStacksCallCount { get; private set; }

    public IReadOnlyDictionary<string, IReadOnlyList<INavigationViewModel>> GetAllNavigationStacks()
    {
        GetAllNavigationStacksCallCount++;
        return AllStacksValue;
    }

    public Task<bool> NavigateToAsync<TViewModel>(
        string regionName = "MainRegion",
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        Calls.Add("NavigateTo");
        NavigateHook?.Invoke();
        return Task.FromResult(NavigateResult);
    }

    public Task<bool> NavigateToAsync<TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        Calls.Add("NavigateToParam");
        NavigateHook?.Invoke();
        return Task.FromResult(NavigateResult);
    }

    public async Task<bool> GoBackAsync(string regionName = "MainRegion", CancellationToken cancellationToken = default)
    {
        if (GoBackHook is not null)
            await GoBackHook();
        return NavigateResult;
    }

    public Task<bool> GoBackToAsync<TViewModel>(
        string regionName = "MainRegion",
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel =>
        Task.FromResult(NavigateResult);

    public Task<bool> GoBackToAsync<TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel =>
        Task.FromResult(NavigateResult);

    public Task<bool> SetRootAsync<TViewModel>(
        string regionName = "MainRegion",
        NavigationRetention retention = NavigationRetention.Destroy,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel =>
        Task.FromResult(NavigateResult);

    public Task<bool> SetRootAsync<TViewModel, TParam>(
        TParam parameter,
        string regionName = "MainRegion",
        NavigationRetention retention = NavigationRetention.Destroy,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel =>
        Task.FromResult(NavigateResult);

    public int Remove<TViewModel>(string regionName = "MainRegion")
        where TViewModel : INavigationViewModel => 0;

    public int Remove<TViewModel, TParam>(TParam parameter, string regionName = "MainRegion")
        where TViewModel : INavigationViewModel => 0;

    public void ClearCache(string regionName)
    {
    }

    public void ClearAllCache()
    {
    }

    public ValueTask ClearCacheAsync(string regionName) => ValueTask.CompletedTask;

    public ValueTask ClearAllCacheAsync() => ValueTask.CompletedTask;

    /// <summary>Invoked inside DisposeAsync, while the disposal is
    /// still in flight, so tests can hold it open on a gate.</summary>
    public Func<Task>? DisposeAsyncHook { get; set; }

    /// <summary>When set, DisposeAsync throws this exception (after the
    /// hook) instead of completing.</summary>
    public Exception? DisposeAsyncException { get; set; }

    public void Dispose() => DisposeCount++;

    public async ValueTask DisposeAsync()
    {
        DisposeAsyncCount++;
        if (DisposeAsyncHook is not null)
            await DisposeAsyncHook();
        if (DisposeAsyncException is not null)
            throw DisposeAsyncException;
    }

    private sealed class Token(Action unsubscribe) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                unsubscribe();
        }
    }
}

public sealed class FakeViewModel : NavigationViewModelBase
{
}
