using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ElegantSeries.Flow.Navigation;

namespace ElegantSeries.Flow.Journal;

/// <summary>
/// The journaling decorator over an <see cref="INavigationService"/>: every
/// member forwards to the wrapped service, and successful navigations are
/// recorded into an <see cref="INavigationJournalStore"/> as
/// <see cref="JournalEntry"/> items. Registered by
/// <c>AddElegantSeriesFlowJournal</c>; application code normally only ever
/// sees the <see cref="INavigationService"/> interface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recording contract.</b> Only navigations whose fate is <see langword="true"/>
/// are recorded. A navigation parameter's snapshot is frozen at call time
/// with the serializer registered for its type (see
/// <see cref="JournalBuilder.RegisterParameter{TParameter}"/>); unregistered
/// types record a null snapshot. Recording never changes a navigation's
/// outcome: a failing serializer or a failing store append is reported
/// through the <see cref="SubscriberError"/> channel (merged with the
/// wrapped service's own reports) and the navigation proceeds untouched.
/// Entries for asynchronous calls are appended before the call completes;
/// entries triggered from synchronous contexts (cache clears, removals,
/// ViewModel releases) are appended by an ordered background queue on a
/// best-effort basis.
/// </para>
/// <para>
/// <b>Snapshot bindings.</b> A stack snapshot describes each page by the
/// parameter and retention the wrapped service actually holds for it,
/// read back from the wrapped service (the effective parameter object
/// and <c>GetCurrentRetention</c>), never by the call's arguments alone:
/// a self-target no-op keeps the page's original binding, and a
/// pop-to-existing reactivation keeps the entry's original retention
/// while taking the new parameter. A binding is applied after the call
/// that landed the page, from that read-back state. When a page settles
/// without a binding — it was registered from a lifecycle callback
/// before it existed, or it bypassed the decorator — the settle
/// announcement fills one in from the same read-back state; an
/// announcement never overwrites an existing binding.
/// </para>
/// <para>
/// <b>Navigations that bypass the decorator.</b> A ViewModel's attached
/// <c>Navigation</c> property always references the wrapped service
/// itself, so navigations issued through it (directly, or through
/// view-model base-class helpers) never pass through this decorator.
/// Settle announcements carry no information about which call — if any —
/// produced a landing, so a bypassed landing is recorded from its
/// announcement (entry appended through the background queue, parameter
/// frozen at settle time rather than at call time) only when the
/// announcement cannot belong to a call this decorator made: no call is
/// in flight for that page type or as a go-back in the region, no
/// callback-registered navigation is still waiting to land that type,
/// and the page was not already recorded by a call. A bypassed landing
/// that coincides with an in-flight call for the same page type may
/// therefore go unrecorded as an entry; its snapshot binding is still
/// captured.
/// </para>
/// <para>
/// <b>Events.</b> The four events are republished from the wrapped service
/// on the same thread that raised them, with the same per-subscriber
/// isolation; subscribers therefore observe exactly the wrapped service's
/// event contract.
/// </para>
/// <para>
/// <b>Disposal.</b> Disposing the decorator unsubscribes it from the wrapped
/// service and forwards disposal to it (exactly once) when the wrapped
/// instance is container-created; an instance supplied directly as a
/// registration instance is not owned and is left alone.
/// <see cref="DisposeAsync"/> first drains the background entry queue;
/// synchronous <see cref="Dispose"/> does not wait for it. Concurrent
/// <see cref="DisposeAsync"/> calls join a single disposal, mirroring the
/// wrapped service: the first call performs the cleanup, callers that
/// arrive while it is in flight await the same operation (and observe the
/// same exception), and callers that arrive after it has completed return
/// immediately. The synchronous <see cref="Dispose"/> never joins an
/// asynchronous disposal and is never joined by one: if it claims the
/// service first it performs the synchronous cleanup (without draining),
/// and an asynchronous operation already in flight then completes without
/// its drain guarantee. A <see cref="DisposeAsync"/> call made on an
/// execution context flowing from within the journal's own recording
/// execution — a store append or a subscriber-error handler running for
/// this service, including work detached from such a callback under its
/// captured context — follows the wrapped service's callback rule instead
/// of starting or joining a disposal: it returns silently once disposal
/// has been claimed, and faults with
/// <see cref="InvalidOperationException"/> before that, because the drain
/// such a disposal performs is waiting on that very execution. (A call from
/// within a page lifecycle callback carries no signal the decorator can
/// see; if it joins an in-flight disposal whose wrapped-service cleanup is
/// waiting on that callback, it completes only when the wrapped service's
/// disposal-gate timeout releases the callback.)
/// </para>
/// </remarks>
public sealed class JournalNavigationService : INavigationService, IDisposable, IAsyncDisposable
{
    private readonly INavigationService _inner;
    private readonly INavigationJournalStore _store;
    private readonly JournalParameterSerializers _serializers;
    private readonly bool _ownsInner;

    private readonly SemaphoreSlim _appendGate = new(1, 1);

    // Append sequencing: sequence numbers are assigned at record-decision
    // time (call completion for call entries, event time for event
    // entries) and appends take turns in sequence order, so the store's
    // order is the sequence order even when awaited appends and the
    // background queue race.
    private readonly object _sequenceLock = new();
    private long _lastAssignedSequence;
    private long _lastAppendedSequence;
    private bool _appendAbandoned;
    private TaskCompletionSource _appendPulse = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The enqueue boundary: Enqueue's disposed check, sequence
    // assignment and channel write are mutually exclusive with the
    // disposal prologues' claim-and-complete step under this lock, so a
    // sequence is only ever assigned to an item that is actually
    // queued, and queued items reach the consumer in sequence order.
    private readonly object _enqueueLock = new();

    private readonly Channel<QueueItem> _queue;
    private readonly Task _queueConsumer;

    // Test seam (null in production): invoked inside the enqueue
    // boundary after the sequence is assigned and before the item is
    // written, so tests can stage an enqueue that is in flight across
    // the start of disposal. Reachable only through the
    // InternalsVisibleTo grant to the test project.
    internal Action? EnqueueBoundaryHook;

    // Test seam (null in production): invoked at the top of the
    // asynchronous disposal core, before it claims the service, so
    // tests can stage a synchronous Dispose winning the claim race.
    internal Action? DisposePrologueHook;

    // Set for the duration of AppendSequencedAsync — the decorator's
    // own recording execution, which runs user code (the store append
    // and the subscriber-error raise) on the queue consumer or on a
    // call-site caller's thread. DisposeAsync consults it to refuse
    // starting or joining a disposal from inside that execution: the
    // drain such a disposal performs waits on this very execution.
    private readonly AsyncLocal<bool> _inRecordingExecution = new();

    private readonly ConditionalWeakTable<INavigationViewModel, PageBinding> _bindings = new();
    private readonly object _bindingsLock = new();
    private readonly HashSet<string> _knownRegions = new(StringComparer.Ordinal);

    // Settle announcements are lossy: the wrapped service announces only
    // the page a region finally settles on, possibly after the producing
    // call already completed, and intermediate landings may never be
    // announced. Announcements therefore never carry bindings or entries
    // for calls — calls record themselves. The state below exists only
    // to decide whether an announcement can be attributed to a call at
    // all; when it provably cannot, the landing bypassed the decorator
    // and is recorded from the announcement.
    //  - in-flight calls per region (forward target types / back count);
    //  - deferred landings: target types of successful calls whose page
    //    had not landed yet when the call completed (registered from a
    //    lifecycle callback), one marker per expected landing;
    //  - instances a call has already recorded (weakly held, so the
    //    marker dies with the page).
    private readonly Dictionary<string, List<Type>> _inFlightTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _inFlightBacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Type>> _deferredLandings = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<INavigationViewModel, object> _recordedInstances = new();
    private static readonly object RecordedInstance = new();
    private readonly object _stateLock = new();

    private readonly object _handlersLock = new();
    private Action<string, INavigationViewModel>? _regionNavigatedHandlers;
    private Action<string, INavigationViewModel>? _viewModelReleasedHandlers;
    private Action<string>? _regionCacheClearedHandlers;
    private Action<Exception>? _subscriberErrorHandlers;

    private readonly IDisposable[] _taps;
    private int _disposed;

    // The single asynchronous disposal operation, shared by every
    // concurrent DisposeAsync caller (the wrapped service's pattern):
    // the first caller starts it, callers that arrive while it is in
    // flight await the same task, and callers that arrive after it has
    // completed return immediately. Guarded by _disposeTaskLock; null
    // until the first DisposeAsync call.
    private readonly object _disposeTaskLock = new();
    private Task? _disposeTask;

    /// <summary>
    /// Creates the decorator. <paramref name="builder"/> supplies the
    /// parameter serializers registered for this journal; the store is
    /// passed separately because its registration is independent of the
    /// navigation service's lifetime.
    /// </summary>
    public JournalNavigationService(INavigationService inner, INavigationJournalStore store, JournalBuilder builder)
        : this(inner, store, builder, ownsInner: true)
    {
    }

    internal JournalNavigationService(INavigationService inner, INavigationJournalStore store, JournalBuilder builder, bool ownsInner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(builder);
        _inner = inner;
        _store = store;
        _serializers = builder.Serializers;
        _ownsInner = ownsInner;

        _queue = Channel.CreateUnbounded<QueueItem>(new UnboundedChannelOptions { SingleReader = true });
        _queueConsumer = Task.Run(ConsumeQueueAsync);

        // Recording taps are subscribed first: a release or an actual
        // cache clear takes its record-decision moment (and sequence)
        // before republished user handlers run, so a handler that
        // re-enters the decorator (e.g. awaits ClearCacheAsync) flushes
        // a queue that already contains the triggering entry.
        _taps =
        [
            _inner.SubscribeViewModelReleased((region, vm) =>
                Enqueue(new PendingEntry(region, JournalEntryKind.Released, vm.GetType().AssemblyQualifiedName, null, null))),
            _inner.SubscribeRegionCacheCleared(region =>
                Enqueue(new PendingEntry(region, JournalEntryKind.CacheCleared, null, null, null))),
            _inner.SubscribeRegionNavigated((region, vm) =>
            {
                TrackRegion(region);
                OnPageSettled(region, vm);
                RaiseHandlers(Snapshot(ref _regionNavigatedHandlers), region, vm);
            }),
            _inner.SubscribeViewModelReleased((region, vm) =>
                RaiseHandlers(Snapshot(ref _viewModelReleasedHandlers), region, vm)),
            _inner.SubscribeRegionCacheCleared(region =>
                RaiseHandlers(Snapshot(ref _regionCacheClearedHandlers), region)),
            _inner.SubscribeSubscriberError(error =>
                RaiseErrorHandlers(SnapshotErrorHandlers(), error)),
        ];
    }

    // ──────────────────────────── Events ────────────────────────────

    /// <inheritdoc/>
    public event Action<string, INavigationViewModel>? RegionNavigated
    {
        add { lock (_handlersLock) _regionNavigatedHandlers += value; }
        remove { lock (_handlersLock) _regionNavigatedHandlers -= value; }
    }

    /// <inheritdoc/>
    public event Action<string, INavigationViewModel>? ViewModelReleased
    {
        add { lock (_handlersLock) _viewModelReleasedHandlers += value; }
        remove { lock (_handlersLock) _viewModelReleasedHandlers -= value; }
    }

    /// <inheritdoc/>
    public event Action<string>? RegionCacheCleared
    {
        add { lock (_handlersLock) _regionCacheClearedHandlers += value; }
        remove { lock (_handlersLock) _regionCacheClearedHandlers -= value; }
    }

    /// <inheritdoc/>
    public event Action<Exception>? SubscriberError
    {
        add { lock (_handlersLock) _subscriberErrorHandlers += value; }
        remove { lock (_handlersLock) _subscriberErrorHandlers -= value; }
    }

    /// <inheritdoc/>
    public IDisposable SubscribeRegionNavigated(Action<string, INavigationViewModel> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlersLock) _regionNavigatedHandlers += handler;
        return new Subscription(() => { lock (_handlersLock) _regionNavigatedHandlers -= handler; });
    }

    /// <inheritdoc/>
    public IDisposable SubscribeViewModelReleased(Action<string, INavigationViewModel> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlersLock) _viewModelReleasedHandlers += handler;
        return new Subscription(() => { lock (_handlersLock) _viewModelReleasedHandlers -= handler; });
    }

    /// <inheritdoc/>
    public IDisposable SubscribeRegionCacheCleared(Action<string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlersLock) _regionCacheClearedHandlers += handler;
        return new Subscription(() => { lock (_handlersLock) _regionCacheClearedHandlers -= handler; });
    }

    /// <inheritdoc/>
    public IDisposable SubscribeSubscriberError(Action<Exception> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlersLock) _subscriberErrorHandlers += handler;
        return new Subscription(() => { lock (_handlersLock) _subscriberErrorHandlers -= handler; });
    }

    // ──────────────────────────── Queries (pure forwarding) ────────────────────────────

    /// <inheritdoc/>
    public bool CanGoBack(string regionName = NavigationDefaults.DefaultRegionName) => _inner.CanGoBack(regionName);

    /// <inheritdoc/>
    public INavigationViewModel? GetCurrentViewModel(string regionName = NavigationDefaults.DefaultRegionName) =>
        _inner.GetCurrentViewModel(regionName);

    /// <inheritdoc/>
    public bool IsActive<TViewModel>(string regionName = NavigationDefaults.DefaultRegionName)
        where TViewModel : INavigationViewModel =>
        _inner.IsActive<TViewModel>(regionName);

    /// <inheritdoc/>
    public NavigationRetention? GetCurrentRetention(string regionName = NavigationDefaults.DefaultRegionName) =>
        _inner.GetCurrentRetention(regionName);

    /// <inheritdoc/>
    public object? GetCurrentParameter(INavigationViewModel viewModel, string regionName = NavigationDefaults.DefaultRegionName) =>
        _inner.GetCurrentParameter(viewModel, regionName);

    /// <inheritdoc/>
    public IReadOnlyList<INavigationViewModel> GetNavigationStack(string regionName = NavigationDefaults.DefaultRegionName) =>
        _inner.GetNavigationStack(regionName);

    // ──────────────────────────── Forward navigation ────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> NavigateToAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = NavigationDefaults.DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        var region = NormalizeRegion(regionName);
        BeginInFlight(region, typeof(TViewModel));
        try
        {
            if (!await _inner.NavigateToAsync<TViewModel>(regionName, operation, retention, refreshIfActive, cancellationToken).ConfigureAwait(false))
                return false;
            TrackRegion(region);
            BindAfterNavigation(region, typeof(TViewModel), passedParameter: null, null, null, retention);
            await AppendNowAsync(new PendingEntry(region, JournalEntryKind.Navigated, typeof(TViewModel).AssemblyQualifiedName, null, null)).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndInFlight(region, typeof(TViewModel));
        }
    }

    /// <inheritdoc/>
    public async Task<bool> NavigateToAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = NavigationDefaults.DefaultRegionName,
        NavigationOperation operation = NavigationOperation.Push,
        NavigationRetention retention = NavigationRetention.Destroy,
        bool refreshIfActive = false,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        var region = NormalizeRegion(regionName);
        var (parameterType, parameterSnapshot) = FreezeParameter(parameter);
        BeginInFlight(region, typeof(TViewModel));
        try
        {
            if (!await _inner.NavigateToAsync<TViewModel, TParam>(parameter, regionName, operation, retention, refreshIfActive, cancellationToken).ConfigureAwait(false))
                return false;
            TrackRegion(region);
            BindAfterNavigation(region, typeof(TViewModel), parameter, parameterType, parameterSnapshot, retention);
            await AppendNowAsync(new PendingEntry(region, JournalEntryKind.Navigated, typeof(TViewModel).AssemblyQualifiedName, parameterType, parameterSnapshot)).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndInFlight(region, typeof(TViewModel));
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetRootAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName = NavigationDefaults.DefaultRegionName,
        NavigationRetention retention = NavigationRetention.Destroy,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        var region = NormalizeRegion(regionName);
        BeginInFlight(region, typeof(TViewModel));
        try
        {
            if (!await _inner.SetRootAsync<TViewModel>(regionName, retention, cancellationToken).ConfigureAwait(false))
                return false;
            TrackRegion(region);
            BindAfterNavigation(region, typeof(TViewModel), passedParameter: null, null, null, retention);
            await AppendNowAsync(new PendingEntry(region, JournalEntryKind.Navigated, typeof(TViewModel).AssemblyQualifiedName, null, null)).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndInFlight(region, typeof(TViewModel));
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetRootAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel, TParam>(
        TParam parameter,
        string regionName = NavigationDefaults.DefaultRegionName,
        NavigationRetention retention = NavigationRetention.Destroy,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        var region = NormalizeRegion(regionName);
        var (parameterType, parameterSnapshot) = FreezeParameter(parameter);
        BeginInFlight(region, typeof(TViewModel));
        try
        {
            if (!await _inner.SetRootAsync<TViewModel, TParam>(parameter, regionName, retention, cancellationToken).ConfigureAwait(false))
                return false;
            TrackRegion(region);
            BindAfterNavigation(region, typeof(TViewModel), parameter, parameterType, parameterSnapshot, retention);
            await AppendNowAsync(new PendingEntry(region, JournalEntryKind.Navigated, typeof(TViewModel).AssemblyQualifiedName, parameterType, parameterSnapshot)).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndInFlight(region, typeof(TViewModel));
        }
    }

    // ──────────────────────────── Backward navigation ────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> GoBackAsync(
        string regionName = NavigationDefaults.DefaultRegionName,
        CancellationToken cancellationToken = default)
    {
        var region = NormalizeRegion(regionName);
        BeginInFlight(region, forwardTarget: null);
        try
        {
            if (!await _inner.GoBackAsync(regionName, cancellationToken).ConfigureAwait(false))
                return false;
            await RecordBackAsync(region).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndInFlight(region, forwardTarget: null);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> GoBackToAsync<TViewModel>(
        string regionName = NavigationDefaults.DefaultRegionName,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        var region = NormalizeRegion(regionName);
        BeginInFlight(region, forwardTarget: null);
        try
        {
            if (!await _inner.GoBackToAsync<TViewModel>(regionName, cancellationToken).ConfigureAwait(false))
                return false;
            await RecordBackAsync(region).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndInFlight(region, forwardTarget: null);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> GoBackToAsync<TViewModel, TParam>(
        TParam parameter,
        string regionName = NavigationDefaults.DefaultRegionName,
        CancellationToken cancellationToken = default)
        where TViewModel : INavigationViewModel
    {
        var region = NormalizeRegion(regionName);
        var (parameterType, parameterSnapshot) = FreezeParameter(parameter);
        BeginInFlight(region, forwardTarget: null);
        try
        {
            if (!await _inner.GoBackToAsync<TViewModel, TParam>(parameter, regionName, cancellationToken).ConfigureAwait(false))
                return false;
            TrackRegion(region);
            UpdateCurrentPageParameter<TViewModel>(region, parameter, parameterType, parameterSnapshot);
            await AppendNowAsync(new PendingEntry(region, JournalEntryKind.Back, LandedViewModelType(region), null, null)).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndInFlight(region, forwardTarget: null);
        }
    }

    // ──────────────────────────── Removal & cache ────────────────────────────

    /// <inheritdoc/>
    public int Remove<TViewModel>(string regionName = NavigationDefaults.DefaultRegionName)
        where TViewModel : INavigationViewModel
    {
        var removed = _inner.Remove<TViewModel>(regionName);
        if (removed > 0)
        {
            var region = NormalizeRegion(regionName);
            TrackRegion(region);
            Enqueue(new PendingEntry(region, JournalEntryKind.Removed, typeof(TViewModel).AssemblyQualifiedName, null, null));
        }
        return removed;
    }

    /// <inheritdoc/>
    public int Remove<TViewModel, TParam>(TParam parameter, string regionName = NavigationDefaults.DefaultRegionName)
        where TViewModel : INavigationViewModel
    {
        var removed = _inner.Remove<TViewModel, TParam>(parameter, regionName);
        if (removed > 0)
        {
            var region = NormalizeRegion(regionName);
            var (parameterType, parameterSnapshot) = FreezeParameter(parameter);
            TrackRegion(region);
            Enqueue(new PendingEntry(region, JournalEntryKind.Removed, typeof(TViewModel).AssemblyQualifiedName, parameterType, parameterSnapshot));
        }
        return removed;
    }

    /// <inheritdoc/>
    public void ClearCache(string regionName) => _inner.ClearCache(regionName);

    /// <inheritdoc/>
    public void ClearAllCache() => _inner.ClearAllCache();

    /// <inheritdoc/>
    public async ValueTask ClearCacheAsync(string regionName)
    {
        await _inner.ClearCacheAsync(regionName).ConfigureAwait(false);
        // The CacheCleared entry arrives through the notification tap into
        // the background queue; flush it so the async tier's completion
        // covers the entry, as the contract promises.
        await FlushQueueAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ClearAllCacheAsync()
    {
        await _inner.ClearAllCacheAsync().ConfigureAwait(false);
        await FlushQueueAsync().ConfigureAwait(false);
    }

    // ──────────────────────────── Disposal ────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        // The claim and the writer completion happen inside the enqueue
        // boundary (see Enqueue): an enqueue that passed its disposed
        // check has already written its item before the writer can
        // complete, so no assigned sequence is ever lost. A call that
        // finds the flag already set — an asynchronous disposal is in
        // flight, or a synchronous one already ran — returns at once:
        // the synchronous path never joins an asynchronous disposal.
        lock (_enqueueLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _queue.Writer.TryComplete();
        }
        UnsubscribeTaps();
        AbandonAppends();
        if (!_ownsInner)
            return;
        if (_inner is IDisposable disposable)
            disposable.Dispose();
        else if (_inner is IAsyncDisposable asyncDisposable)
            _ = ObserveAsyncOnlyDisposal(asyncDisposable);
    }

    // A wrapped service that is only IAsyncDisposable cannot be disposed
    // synchronously; its disposal is observed so a fault reaches the
    // SubscriberError channel instead of becoming an unobserved task
    // exception. (The Core service implements both interfaces; this path
    // exists for custom implementations.)
    private async Task ObserveAsyncOnlyDisposal(IAsyncDisposable inner)
    {
        try
        {
            await inner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseErrorHandlers(SnapshotErrorHandlers(), ex);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? starter = null;
        Task shared;
        lock (_disposeTaskLock)
        {
            if (_inRecordingExecution.Value)
            {
                // Called from within the decorator's own recording
                // execution — a store append or a subscriber-error
                // handler running for this service, on the queue
                // consumer during a drain or on a call-site append the
                // consumer may be turn-waiting for. Starting or joining
                // a disposal here would wait on that very execution and
                // never complete: the wrapped service's
                // lifecycle-callback rule, applied to the decorator's
                // own callbacks. Once disposal has been claimed, return
                // silently; before that, fault instead of starting.
                return (_disposeTask is not null || Volatile.Read(ref _disposed) != 0)
                    ? default
                    : RecordingReentrancyFault();
            }

            if (_disposeTask is { IsCompleted: true } || (_disposeTask is null && Volatile.Read(ref _disposed) != 0))
            {
                // The shared disposal already completed, or a synchronous
                // Dispose claimed the service (it never starts this task)
                // and owns the cleanup. Nothing is left to wait for.
                return default;
            }

            if (_disposeTask is { IsCompleted: false } inFlight)
            {
                // Join the in-flight disposal instead of returning early,
                // so this caller's completion means the drain and the
                // wrapped service's disposal have actually finished.
                return new ValueTask(inFlight);
            }

            starter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = starter.Task;
            shared = starter.Task;
        }

        // The core starts OUTSIDE the lock and off the caller's thread:
        // its prologue contends for the enqueue boundary — a lock a
        // caller thread must never block on inside an async API — and
        // its drain runs store code, so neither the first caller nor a
        // joining one may be held up by the cleanup's prologue.
        _ = Task.Run(() => RunDisposeAsync(starter!));
        return new ValueTask(shared);
    }

    // The reentrancy failure travels in the returned ValueTask rather
    // than throwing synchronously (the wrapped service's shape), so a
    // caller that discards the result without awaiting it is not
    // faulted at the call site.
    private static ValueTask RecordingReentrancyFault() => new(Task.FromException(new InvalidOperationException(
        "DisposeAsync cannot be called from within a journal store append or subscriber-error handler. " +
        "Call it outside the callback instead.")));

    private async Task RunDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeAsyncCore()
    {
        DisposePrologueHook?.Invoke();
        bool claimed;
        lock (_enqueueLock)
        {
            claimed = Interlocked.Exchange(ref _disposed, 1) == 0;
            if (claimed)
                _queue.Writer.TryComplete();
        }

        if (!claimed)
        {
            // A synchronous Dispose claimed the service first and owns
            // the cleanup; this operation completes without draining,
            // exactly as a DisposeAsync that arrives during a synchronous
            // cleanup does (the wrapped service's rule).
            return;
        }

        UnsubscribeTaps();
        // Drain: queued entries land before the wrapped service is
        // disposed. Only then are racing call-site appends abandoned.
        await _queueConsumer.ConfigureAwait(false);
        AbandonAppends();
        if (_ownsInner)
        {
            if (_inner is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (_inner is IDisposable disposable)
                disposable.Dispose();
        }
        // The append gate is deliberately not disposed: an in-flight
        // call-site append may still be waiting on it, and disposing it
        // would fault that (already successful) navigation with
        // ObjectDisposedException. The gate's wait handle is never
        // materialized, so there is nothing for disposal to release.
    }

    // ──────────────────────────── Snapshot support (for NavigationJournal) ────────────────────────────

    internal async Task<JournalSnapshot> CaptureSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        await _appendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] regions;
            lock (_stateLock)
                regions = [.. _knownRegions];
            var regionSnapshots = new List<JournalRegionSnapshot>();
            foreach (var region in regions)
            {
                var stack = _inner.GetNavigationStack(region);
                if (stack.Count == 0)
                    continue;
                var pages = new List<JournalPageSnapshot>(stack.Count);
                foreach (var viewModel in stack)
                {
                    TryGetBinding(viewModel, out var binding);
                    pages.Add(new JournalPageSnapshot
                    {
                        ViewModelType = viewModel.GetType().AssemblyQualifiedName!,
                        ParameterType = binding?.ParameterType,
                        Parameter = binding?.ParameterSnapshot,
                        // Pages not navigated through the journal (only
                        // possible when application code kept a direct
                        // reference to the wrapped service) carry no
                        // binding: no parameter, Destroy retention.
                        Retention = binding?.Retention ?? NavigationRetention.Destroy,
                    });
                }
                regionSnapshots.Add(new JournalRegionSnapshot { Region = region, Pages = pages });
            }
            return new JournalSnapshot
            {
                FormatVersion = 1,
                CapturedUtc = DateTimeOffset.UtcNow,
                Regions = regionSnapshots,
            };
        }
        finally
        {
            _appendGate.Release();
        }
    }

    internal JournalParameterSerializers Serializers => _serializers;

    // ──────────────────────────── Recording internals ────────────────────────────

    private async Task RecordBackAsync(string region)
    {
        TrackRegion(region);
        // The landed page's recording is this Back entry: mark the
        // instance so a later announcement does not synthesize a
        // second, Navigated entry for the same landing.
        if (_inner.GetCurrentViewModel(region) is { } landed)
            MarkRecorded(landed);
        await AppendNowAsync(new PendingEntry(region, JournalEntryKind.Back, LandedViewModelType(region), null, null)).ConfigureAwait(false);
    }

    private string? LandedViewModelType(string region) =>
        _inner.GetCurrentViewModel(region)?.GetType().AssemblyQualifiedName;

    private (string? ParameterType, string? ParameterSnapshot) FreezeParameter<TParam>(TParam parameter) =>
        FreezeParameter(parameter, reportErrors: true);

    private (string? ParameterType, string? ParameterSnapshot) FreezeParameter<TParam>(TParam parameter, bool reportErrors)
    {
        if (parameter is null)
            return (null, null);
        var parameterType = parameter.GetType().AssemblyQualifiedName;
        try
        {
            return _serializers.TrySerialize(parameter, out var snapshot)
                ? (parameterType, snapshot)
                : (parameterType, null);
        }
        catch (Exception ex)
        {
            // A failing serializer must not touch the navigation: record
            // the parameter type with a null snapshot and report (unless
            // the caller already reported this parameter's freeze).
            if (reportErrors)
                RaiseErrorHandlers(SnapshotErrorHandlers(), ex);
            return (parameterType, null);
        }
    }

    // Settle-announcement handling. The announcement ALWAYS fills in a
    // binding for a settled page that has none — whichever call (if
    // any) produced the landing, an unbound settled page would
    // otherwise surface in snapshots with no parameter and Destroy
    // retention. The binding derives from the page's effective state
    // and never overwrites a call-side binding. An entry, by contrast,
    // is synthesized only when the landing provably came from no call:
    // no call is in flight for the page's type or as a go-back in the
    // region, no deferred landing was waiting for the page's type, and
    // the page instance was not already recorded by a call.
    private void OnPageSettled(string region, INavigationViewModel settled)
    {
        bool attributedToCall;
        var deferredLanding = false;
        lock (_stateLock)
        {
            attributedToCall = _inFlightBacks.GetValueOrDefault(region) > 0 ||
                (_inFlightTargets.TryGetValue(region, out var targets) &&
                 targets.Any(target => target.IsAssignableFrom(settled.GetType())));
            if (!attributedToCall && _deferredLandings.TryGetValue(region, out var deferred))
            {
                var index = deferred.FindIndex(target => target.IsAssignableFrom(settled.GetType()));
                if (index >= 0)
                {
                    deferred.RemoveAt(index);
                    if (deferred.Count == 0)
                        _deferredLandings.Remove(region);
                    deferredLanding = true;
                }
            }
        }

        // The freeze that fills the binding reports a failing
        // serializer only when the landing came from no call: a call's
        // own freeze (at call time) is the one that reports, exactly
        // once, for its landing and for a deferred landing alike.
        var (parameterType, parameterSnapshot) = BindIfUnbound(
            region, settled, reportFreezeErrors: !attributedToCall && !deferredLanding);
        if (attributedToCall || deferredLanding)
            return;
        if (_recordedInstances.TryGetValue(settled, out _))
            return;

        Enqueue(new PendingEntry(
            region, JournalEntryKind.Navigated, settled.GetType().AssemblyQualifiedName,
            parameterType, parameterSnapshot));
        MarkRecorded(settled);
    }

    // Fills in a snapshot binding for a settled page that has none, from
    // the state the wrapped service effectively holds for it (parameter
    // object via GetCurrentParameter, frozen now; retention via
    // GetCurrentRetention), and returns the bound parameter description
    // (the existing binding's, when one was already present). Existing
    // bindings are never overwritten: call-side bindings carry the
    // call-time frozen snapshot and win.
    private (string? ParameterType, string? ParameterSnapshot) BindIfUnbound(
        string region, INavigationViewModel settled, bool reportFreezeErrors)
    {
        if (TryGetBinding(settled, out var existing))
            return (existing!.ParameterType, existing.ParameterSnapshot);
        string? parameterType = null;
        string? parameterSnapshot = null;
        var parameter = _inner.GetCurrentParameter(settled, region);
        if (parameter is not null)
            (parameterType, parameterSnapshot) = FreezeParameter(parameter, reportFreezeErrors);
        var retention = _inner.GetCurrentRetention(region) ?? NavigationRetention.Destroy;
        lock (_bindingsLock)
        {
            if (_bindings.TryGetValue(settled, out var raced))
                return (raced.ParameterType, raced.ParameterSnapshot);
            _bindings.Add(settled, new PageBinding
            {
                ParameterType = parameterType,
                ParameterSnapshot = parameterSnapshot,
                Retention = retention,
            });
        }
        return (parameterType, parameterSnapshot);
    }

    // Post-call binding for a successful forward navigation. The wrapped
    // service, not the call's arguments, decides the effective parameter
    // and retention:
    //  - the landed page is identified by assignability to the call's
    //    target type; a different (or missing) current page means the
    //    call was registered from a lifecycle callback — its page lands
    //    later — or was overtaken by a concurrent navigation, so a
    //    deferred-landing marker is left for the settle announcement
    //    instead of a binding;
    //  - retention is read back via GetCurrentRetention, so a
    //    pop-to-existing reactivation keeps the entry's original
    //    retention instead of the call's;
    //  - the frozen snapshot is bound only when the page's effective
    //    parameter (GetCurrentParameter) is the object this call passed,
    //    so a self-target no-op — which the wrapped service completes
    //    WITHOUT applying the new parameter — keeps the original binding.
    private void BindAfterNavigation(
        string region,
        Type targetType,
        object? passedParameter,
        string? parameterType,
        string? parameterSnapshot,
        NavigationRetention fallbackRetention)
    {
        var current = _inner.GetCurrentViewModel(region);
        if (current is null || !targetType.IsAssignableFrom(current.GetType()))
        {
            AddDeferredLanding(region, targetType);
            return;
        }
        MarkRecorded(current);

        var retention = _inner.GetCurrentRetention(region) ?? fallbackRetention;
        var effective = _inner.GetCurrentParameter(current, region);
        var applied = effective is null
            ? passedParameter is null
            : passedParameter is not null &&
              (ReferenceEquals(effective, passedParameter) || effective.Equals(passedParameter));
        if (applied)
        {
            SetBinding(current, parameterType, parameterSnapshot, retention);
        }
        else if (!TryGetBinding(current, out _))
        {
            // The page kept a parameter this call did not supply and has
            // no binding yet (it was created outside the journal): record
            // its retention only. Serializing the effective parameter now
            // would break the call-time freeze rule.
            SetBinding(current, null, null, retention);
        }
        // Otherwise the existing binding already describes the page's
        // effective parameter; leave it untouched.
    }

    private void UpdateCurrentPageParameter<TViewModel>(
        string region,
        object? passedParameter,
        string? parameterType,
        string? parameterSnapshot)
        where TViewModel : INavigationViewModel
    {
        var current = _inner.GetCurrentViewModel(region);
        if (current is not TViewModel)
            return;
        // This call's Back entry records the landing regardless of
        // whether the parameter update below applies.
        MarkRecorded(current);
        var effective = _inner.GetCurrentParameter(current, region);
        // Same applied test as the forward path: the new parameter is
        // bound only when the landed page's effective parameter is the
        // object this call passed (a non-claimed GoBackTo leaves the
        // page's stored parameter — and its binding — untouched).
        var applied = effective is null
            ? passedParameter is null
            : passedParameter is not null &&
              (ReferenceEquals(effective, passedParameter) || effective.Equals(passedParameter));
        if (!applied)
            return; // the landed page kept its previous parameter
        var retention = TryGetBinding(current, out var existing)
            ? existing!.Retention
            : _inner.GetCurrentRetention(region) ?? NavigationRetention.Destroy;
        SetBinding(current, parameterType, parameterSnapshot, retention);
    }

    private void SetBinding(
        INavigationViewModel viewModel,
        string? parameterType,
        string? parameterSnapshot,
        NavigationRetention retention)
    {
        // The remove/add pair runs under the bindings lock: a concurrent
        // binding of the same instance cannot interleave into a
        // duplicate-key failure, and a capture never observes the gap
        // between the two operations. Bindings themselves are immutable
        // and replaced wholesale, so a held binding is never torn.
        lock (_bindingsLock)
        {
            _bindings.Remove(viewModel);
            _bindings.Add(viewModel, new PageBinding
            {
                ParameterType = parameterType,
                ParameterSnapshot = parameterSnapshot,
                Retention = retention,
            });
        }
    }

    private bool TryGetBinding(INavigationViewModel viewModel, out PageBinding? binding)
    {
        lock (_bindingsLock)
            return _bindings.TryGetValue(viewModel, out binding);
    }

    private void BeginInFlight(string region, Type? forwardTarget)
    {
        lock (_stateLock)
        {
            if (forwardTarget is not null)
            {
                if (!_inFlightTargets.TryGetValue(region, out var list))
                    _inFlightTargets[region] = list = [];
                list.Add(forwardTarget);
            }
            else
            {
                _inFlightBacks[region] = _inFlightBacks.GetValueOrDefault(region) + 1;
            }
        }
    }

    private void EndInFlight(string region, Type? forwardTarget)
    {
        lock (_stateLock)
        {
            if (forwardTarget is not null)
            {
                if (!_inFlightTargets.TryGetValue(region, out var list))
                    return;
                list.Remove(forwardTarget);
                if (list.Count == 0)
                    _inFlightTargets.Remove(region);
            }
            else
            {
                var count = _inFlightBacks.GetValueOrDefault(region);
                if (count <= 1)
                    _inFlightBacks.Remove(region);
                else
                    _inFlightBacks[region] = count - 1;
            }
        }
    }

    private void AddDeferredLanding(string region, Type targetType)
    {
        lock (_stateLock)
        {
            if (!_deferredLandings.TryGetValue(region, out var list))
                _deferredLandings[region] = list = [];
            list.Add(targetType);
        }
    }

    private void MarkRecorded(INavigationViewModel viewModel) =>
        _recordedInstances.GetValue(viewModel, static _ => RecordedInstance);

    private void TrackRegion(string region)
    {
        lock (_stateLock)
            _knownRegions.Add(region);
    }

    private static string NormalizeRegion(string regionName)
    {
        // Mirrors the wrapped service's normalization and validation
        // exactly (null → ArgumentNullException, whitespace →
        // ArgumentException), so an invalid region fails the same way
        // decorated or not — and before any parameter is frozen.
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        return regionName.Trim();
    }

    private long AssignSequence()
    {
        lock (_sequenceLock)
            return ++_lastAssignedSequence;
    }

    private void AbandonAppends()
    {
        lock (_sequenceLock)
        {
            _appendAbandoned = true;
            var pulse = _appendPulse;
            _appendPulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pulse.TrySetResult();
        }
    }

    private async ValueTask AppendNowAsync(PendingEntry pending)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        // The sequence is assigned synchronously at the call site (this
        // prologue runs on the caller's thread before the first await),
        // which is the entry's record-decision moment.
        await AppendSequencedAsync(pending with { Sequence = AssignSequence() }).ConfigureAwait(false);
    }

    private async ValueTask AppendSequencedAsync(PendingEntry pending)
    {
        // Everything the core runs is the decorator's own recording
        // execution — the turn wait, the store append (user code) and
        // the error-handler raise (user code). The marker lets
        // DisposeAsync refuse to start or join a disposal from inside
        // it. Restored on exit, so the caller's context is unchanged.
        var previous = _inRecordingExecution.Value;
        _inRecordingExecution.Value = true;
        try
        {
            await AppendSequencedCoreAsync(pending).ConfigureAwait(false);
        }
        finally
        {
            _inRecordingExecution.Value = previous;
        }
    }

    private async ValueTask AppendSequencedCoreAsync(PendingEntry pending)
    {
        // No disposed check here: DisposeAsync drains the queue through
        // this path after disposal has begun. The abandon flag alone
        // controls dropping.
        // Wait for this entry's turn: every lower sequence must land
        // first. Abandoned (disposal) drops the entry silently, matching
        // the post-disposal boundary.
        while (true)
        {
            Task wait = Task.CompletedTask;
            lock (_sequenceLock)
            {
                if (_appendAbandoned || _lastAppendedSequence == pending.Sequence - 1)
                    break;
                wait = _appendPulse.Task;
            }
            await wait.ConfigureAwait(false);
        }

        Exception? failure = null;
        var appended = false;
        await _appendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sequenceLock)
            {
                if (_appendAbandoned)
                    return;
            }
            var entry = new JournalEntry
            {
                Sequence = pending.Sequence,
                TimestampUtc = DateTimeOffset.UtcNow,
                Region = pending.Region,
                Kind = pending.Kind,
                ViewModelType = pending.ViewModelType,
                ParameterType = pending.ParameterType,
                ParameterSnapshot = pending.ParameterSnapshot,
            };
            await _store.AppendAsync(entry).ConfigureAwait(false);
            appended = true;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            _appendGate.Release();
            // Even a failed append advances the sequence: later entries
            // must not stall behind a broken store.
            lock (_sequenceLock)
            {
                if (appended || failure is not null)
                    _lastAppendedSequence = pending.Sequence;
                var pulse = _appendPulse;
                _appendPulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                pulse.TrySetResult();
            }
        }
        // Report after releasing the gate: an error handler that navigates
        // would otherwise wait on its own recording.
        if (failure is not null)
            RaiseErrorHandlers(SnapshotErrorHandlers(), failure);
    }

    private void Enqueue(PendingEntry pending)
    {
        // The disposed check, the sequence assignment and the channel
        // write form one boundary with the disposal prologues, which
        // claim the disposed flag and complete the writer under the
        // same lock. A sequence is therefore only assigned to an item
        // that is actually queued — the write below cannot fail on an
        // unbounded channel whose completion is lock-ordered after the
        // check — and items reach the consumer in sequence order, so
        // the consumer never waits for a sequence that will never
        // arrive or for one still sitting unread in the channel.
        lock (_enqueueLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            var sequenced = pending with { Sequence = AssignSequence() };
            EnqueueBoundaryHook?.Invoke();
            _queue.Writer.TryWrite(new EntryItem(sequenced));
        }
    }

    private async Task FlushQueueAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_queue.Writer.TryWrite(new FlushItem(flushed)))
            await flushed.Task.ConfigureAwait(false);
    }

    private async Task ConsumeQueueAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            switch (item)
            {
                case EntryItem entry:
                    await AppendSequencedAsync(entry.Pending).ConfigureAwait(false);
                    break;
                case FlushItem flush:
                    flush.Completion.TrySetResult();
                    break;
            }
        }
    }

    private void UnsubscribeTaps()
    {
        foreach (var tap in _taps)
            tap.Dispose();
    }

    // ──────────────────────────── Event raising (per-subscriber isolation) ────────────────────────────

    private Action<string, INavigationViewModel>? Snapshot(ref Action<string, INavigationViewModel>? field)
    {
        lock (_handlersLock)
            return field;
    }

    private Action<string>? Snapshot(ref Action<string>? field)
    {
        lock (_handlersLock)
            return field;
    }

    private Action<Exception>? SnapshotErrorHandlers()
    {
        lock (_handlersLock)
            return _subscriberErrorHandlers;
    }

    private void RaiseHandlers(Action<string, INavigationViewModel>? handlers, string region, INavigationViewModel viewModel)
    {
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<string, INavigationViewModel>>())
        {
            try
            {
                handler(region, viewModel);
            }
            catch (Exception ex)
            {
                RaiseErrorHandlers(SnapshotErrorHandlers(), ex);
            }
        }
    }

    private void RaiseHandlers(Action<string>? handlers, string region)
    {
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<string>>())
        {
            try
            {
                handler(region);
            }
            catch (Exception ex)
            {
                RaiseErrorHandlers(SnapshotErrorHandlers(), ex);
            }
        }
    }

    private void RaiseErrorHandlers(Action<Exception>? handlers, Exception error)
    {
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<Exception>>())
        {
            try
            {
                handler(error);
            }
            catch
            {
                // Error handlers must not cascade: a throwing SubscriberError
                // handler is swallowed (same rule as the wrapped service).
            }
        }
    }

    private sealed class PageBinding
    {
        public string? ParameterType { get; init; }
        public string? ParameterSnapshot { get; init; }
        public NavigationRetention Retention { get; init; }
    }

    private sealed record PendingEntry(
        string Region,
        JournalEntryKind Kind,
        string? ViewModelType,
        string? ParameterType,
        string? ParameterSnapshot,
        long Sequence = 0);

    private abstract record QueueItem;
    private sealed record EntryItem(PendingEntry Pending) : QueueItem;
    private sealed record FlushItem(TaskCompletionSource Completion) : QueueItem;

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                unsubscribe();
        }
    }
}
