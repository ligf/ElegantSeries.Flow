using System.Diagnostics.CodeAnalysis;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — the window model: navigation requests are
/// registered as intents into a per-region single slot and executed by a
/// per-region pump at page-lifecycle phase boundaries.
/// </summary>
/// <remarks>
/// A page's lifecycle window runs from the moment a hop lands on it until its
/// <c>OnNavigatedFrom</c> callback completes. While a window is open, navigation
/// requests for that region — from the page's own callbacks, from page code
/// (buttons), or from anywhere else — do not execute; they are written into the
/// region's intent slot, each new request superseding the previous one. When the
/// window closes (the From callback completes), the pump executes the last
/// registered intent: it constructs the target page, which opens its own window,
/// so redirect chains advance one fully-constructed page at a time. A request
/// that arrives while the region rests on a page starts that page's departure
/// immediately. Superseded requests never construct a ViewModel and never run
/// lifecycle callbacks.
/// </remarks>
public sealed partial class NavigationService
{
    // ────────────────────────── Window state ──────────────────────────

    private enum WindowPhase
    {
        /// <summary>No current page in the region (empty stack).</summary>
        None,

        /// <summary>The current page's To callback is running.</summary>
        Entering,

        /// <summary>The current page rests as the region's active page.</summary>
        Open,

        /// <summary>A departure is in flight (guard or From callback running).</summary>
        Leaving,
    }

    /// <summary>
    /// Per-region window state. The slot and phase fields are guarded by
    /// <see cref="_stateLock"/>; the <c>Entering*</c> fields are pump-private
    /// (only the region's pump loop reads and writes them, across its own awaits).
    /// </summary>
    private sealed class RegionWindow
    {
        public WindowPhase Phase = WindowPhase.None;
        public NavigationIntent? PendingIntent;
        public bool PumpActive;

        /// <summary>
        /// Ownership token of the pump run that currently owns
        /// <see cref="PumpActive"/>. A pump's exit path may only release or
        /// hand over the flag while it still owns it: a registration that
        /// interposes after the settling step has released the flag starts a
        /// successor pump with its own token, and the predecessor's exit path
        /// must not clear the successor's flag (which would let yet another
        /// pump start and run a concurrent departure for the same page).
        /// </summary>
        public object? PumpOwner;
        public int ChainDepartures;

        /// <summary>
        /// True once the region's current top page has been surfaced via
        /// <see cref="INavigationService.RegionNavigated"/> since it landed.
        /// Reset by SetEntering on every landing; a departure that is refused
        /// for a page that never surfaced must surface it when it comes to
        /// rest, or hosts keep showing the previous page.
        /// </summary>
        public bool CurrentPageSurfaced;
        public readonly List<string> ChainTrace = [];

        /// <summary>
        /// Completed by a registration that lands while the window is entering
        /// or leaving; replaced by the pump at each phase start. Guarded by
        /// <see cref="_stateLock"/>. Lets the pump notice promptly that a newer
        /// request wants the window closed while a lifecycle callback of the
        /// current page is still in flight.
        /// </summary>
        public TaskCompletionSource? CloseSignal;

        /// <summary>
        /// Completed when the service is disposed (see DrainWindowIntents), so
        /// pump waits that disposal cannot otherwise reach — a leave guard
        /// still in flight — observe it and settle instead of hanging past
        /// Dispose. Guards race against this signal in addition to the
        /// candidate's token.
        /// </summary>
        public readonly TaskCompletionSource DisposalSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pump-private: the hop that landed on the entering page, completed by
        // the entering step after the page's To callback ran.
        public NavigationEntry? EnteringEntry;
        public Task? EnteringGate;
        public NavigationIntent? EnteredIntent;
        public List<Exception>? EnteredErrors;
        public TransitionWork? EnteredWork;
    }

    /// <summary>Window state per region. Guarded by <see cref="_stateLock"/>.</summary>
    private readonly Dictionary<string, RegionWindow> _windows = [];

    /// <summary>The caller must hold <see cref="_stateLock"/>.</summary>
    private RegionWindow GetOrCreateWindowLocked(string regionName)
    {
        if (!_windows.TryGetValue(regionName, out var window))
        {
            window = new RegionWindow();
            _windows[regionName] = window;
        }

        return window;
    }

    /// <summary>
    /// Removes the window from <see cref="_windows"/> when the region has
    /// nothing left to track: the pump flag is released, no intent is
    /// pending, the window is at rest, and the region's stack is empty (or
    /// absent). The caller must hold <see cref="_stateLock"/> and must have
    /// released the window's pump flag in the same critical section, so the
    /// removal is atomic against <see cref="RegisterIntent"/>: a later
    /// registration either lands in this window before the removal (and
    /// keeps it alive) or recreates a fresh window after it. Windows of
    /// regions with live pages stay — their surfacing and chain state
    /// belongs to the resting page.
    /// </summary>
    private void RemoveWindowIfIdleLocked(RegionWindow window, string regionName)
    {
        if (!window.PumpActive
            && window.PendingIntent is null
            && window.Phase is WindowPhase.Open or WindowPhase.None
            && (!_regionStacks.TryGetValue(regionName, out var stack) || stack.Count == 0)
            && _windows.TryGetValue(regionName, out var current)
            && ReferenceEquals(current, window))
        {
            _windows.Remove(regionName);
        }
    }

    // ────────────────────────── Intents ──────────────────────────

    private enum NavigationIntentKind
    {
        Navigate,
        GoBack,
        GoBackTo,
    }

    /// <summary>
    /// One registered navigation request: pure data until the pump executes it.
    /// A superseded intent is discarded without any side effect — no ViewModel
    /// was constructed for it and no lifecycle callback ran.
    /// </summary>
    private sealed class NavigationIntent
    {
        public required string RegionName { get; init; }
        public required NavigationIntentKind Kind { get; init; }

        /// <summary>
        /// True when the request was registered from within a lifecycle
        /// callback or leave guard. Only hops driven by such requests
        /// count toward the redirect-chain limit: a hop whose winning
        /// request came from outside code has an external actor in the loop
        /// and restarts the accounting.
        /// </summary>
        public required bool RegisteredInCallback { get; init; }

        public Type? TargetType { get; init; }
        public NavigationOperation Operation { get; init; }
        public NavigationRetention Retention { get; init; }
        public object? Parameter { get; init; }

        /// <summary>
        /// True for a parameterized <c>GoBackTo</c> intent: the target search
        /// runs the recognition gate with <see cref="Parameter"/> instead of
        /// accepting the nearest entry of the target type. (Navigate intents
        /// distinguish the two shapes by their entry points instead.)
        /// </summary>
        public bool HasParameter { get; init; }
        public bool RefreshIfActive { get; init; }
        public CancellationToken CancellationToken { get; init; }

        /// <summary>
        /// Completes with the intent's own fate (executed → true; superseded or
        /// vetoed → false; execution failure → faulted). Null for requests
        /// registered from within a lifecycle callback: those complete at
        /// registration time, because the pump cannot run until the callback
        /// returns and awaiting the fate from inside the callback would deadlock.
        /// </summary>
        public TaskCompletionSource<bool>? Fate { get; init; }

        /// <summary>
        /// Executes one hop for a Navigate intent (resolution + stack surgery +
        /// disposals). Supplied by the generic registration entry point. The
        /// executing pump passes its own window: the window an intent was
        /// registered into is not necessarily the window that executes it
        /// (an idle window may have been reclaimed and recreated in between).
        /// </summary>
        public Func<NavigationIntent, RegionWindow, TransitionWork, List<Exception>, Task<HopResult>>? ExecuteHop { get; init; }

        public bool IsWithdrawn => CancellationToken.IsCancellationRequested;

        public string Describe() => Kind switch
        {
            NavigationIntentKind.GoBack => "GoBack",
            NavigationIntentKind.GoBackTo => $"GoBackTo({TargetType?.Name ?? "?"})",
            _ => TargetType?.Name ?? "?",
        };
    }

    /// <summary>
    /// The outcome of executing one hop: the entry whose window now enters (its
    /// To registration was made during the commit) plus the hop's work bucket,
    /// or a null entry when the hop did not land on a new window (the current
    /// page was revived, or the hop failed and its fate was already completed).
    /// </summary>
    private readonly record struct HopResult(NavigationEntry? Entry, Task? ToGate, TransitionWork? Work);

    // ────────────────────────── Registration ──────────────────────────

    /// <summary>
    /// Entry point behind <see cref="NavigateToAsync{TViewModel}"/>: registers a
    /// navigation intent for the region and returns the intent's fate.
    /// </summary>
    private async Task<bool> NavigateIntentAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        string regionName,
        object? parameter,
        NavigationOperation operation,
        NavigationRetention retention,
        bool refreshIfActive,
        CancellationToken cancellationToken)
        where TViewModel : INavigationViewModel
    {
        regionName = NormalizeRegionName(regionName);
        bool inCallback = CurrentOwnLifecycleEntry is not null;
        if (_disposed)
        {
            if (inCallback) return false;
            ThrowIfDisposed();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var intent = new NavigationIntent
        {
            RegionName = regionName,
            Kind = NavigationIntentKind.Navigate,
            RegisteredInCallback = inCallback,
            TargetType = typeof(TViewModel),
            Operation = operation,
            Retention = retention,
            Parameter = parameter,
            RefreshIfActive = refreshIfActive,
            CancellationToken = cancellationToken,
            Fate = inCallback ? null : new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            ExecuteHop = (i, w, work, errors) => ExecuteNavigateHopAsync<TViewModel>(i, work, errors, w),
        };

        RegisterIntent(intent);
        if (inCallback)
        {
            // Registered: the request takes effect when the current page's
            // window closes. Its fate is not awaitable from inside a callback.
            return true;
        }

        return await AwaitIntentFateAsync(intent).ConfigureAwait(false);
    }

    /// <summary>
    /// Entry point behind <see cref="GoBackAsync"/>: registers a back intent for
    /// the region and returns the intent's fate.
    /// </summary>
    private async Task<bool> GoBackIntentAsync(string regionName, CancellationToken cancellationToken)
    {
        regionName = NormalizeRegionName(regionName);
        bool inCallback = CurrentOwnLifecycleEntry is not null;
        if (_disposed)
        {
            if (inCallback) return false;
            ThrowIfDisposed();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var intent = new NavigationIntent
        {
            RegionName = regionName,
            Kind = NavigationIntentKind.GoBack,
            RegisteredInCallback = inCallback,
            CancellationToken = cancellationToken,
            Fate = inCallback ? null : new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            ExecuteHop = (i, w, work, errors) => ExecuteGoBackHopAsync(i, work, errors, w),
        };

        RegisterIntent(intent);
        if (inCallback)
        {
            return true;
        }

        return await AwaitIntentFateAsync(intent).ConfigureAwait(false);
    }

    /// <summary>
    /// Entry point behind <c>GoBackToAsync</c>: registers a back-to intent for
    /// the region and returns the intent's fate. The target search itself
    /// runs later — at departure time and again in the executing hop —
    /// against the stack as it then stands.
    /// </summary>
    private async Task<bool> GoBackToIntentAsync<TViewModel>(
        string regionName,
        bool hasParameter,
        object? parameter,
        CancellationToken cancellationToken)
        where TViewModel : INavigationViewModel
    {
        regionName = NormalizeRegionName(regionName);
        bool inCallback = CurrentOwnLifecycleEntry is not null;
        if (_disposed)
        {
            if (inCallback) return false;
            ThrowIfDisposed();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var intent = new NavigationIntent
        {
            RegionName = regionName,
            Kind = NavigationIntentKind.GoBackTo,
            RegisteredInCallback = inCallback,
            TargetType = typeof(TViewModel),
            HasParameter = hasParameter,
            Parameter = parameter,
            CancellationToken = cancellationToken,
            Fate = inCallback ? null : new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            ExecuteHop = (i, w, work, errors) => ExecuteGoBackToHopAsync(i, work, errors, w),
        };

        RegisterIntent(intent);
        if (inCallback)
        {
            return true;
        }

        return await AwaitIntentFateAsync(intent).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the entry a <c>GoBackTo</c> intent is addressed to within a
    /// stack snapshot (top first): the nearest entry whose navigation target
    /// type is the intent's type and — for a parameterized intent — that the
    /// recognition gate claims for the intent's parameter. There is no
    /// KeepAlive restriction: any entry on the stack can be the target.
    /// Classification invokes user code; the caller must not hold
    /// <see cref="_stateLock"/>.
    /// </summary>
    private static NavigationEntry? FindGoBackToTarget(
        List<NavigationEntry> snapshotTopFirst, NavigationIntent intent, List<Exception> classificationErrors)
    {
        foreach (var entry in snapshotTopFirst)
        {
            if (entry.CacheKeyType != intent.TargetType)
            {
                continue;
            }

            if (!intent.HasParameter)
            {
                return entry;
            }

            if (ClassifyCandidate(entry, intent.Parameter, classificationErrors) == RecognitionVerdict.Recognized)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the intent into the region's slot (superseding any pending intent)
    /// and starts the region's pump when the region is at rest. Lifecycle
    /// callbacks and disposal never run here — only the pump executes.
    /// </summary>
    private void RegisterIntent(NavigationIntent intent)
    {
        NavigationIntent? superseded;
        RegionWindow window;
        bool startPump = false;
        object? pumpOwner = null;
        using (_stateLock.EnterScope())
        {
            window = GetOrCreateWindowLocked(intent.RegionName);
            superseded = window.PendingIntent;
            window.PendingIntent = intent;
            if (window.Phase is WindowPhase.Entering or WindowPhase.Leaving)
            {
                // A newer request wants the current window closed: wake the
                // pump, which may be awaiting an in-flight lifecycle callback.
                window.CloseSignal?.TrySetResult();
            }

            if (!window.PumpActive && window.Phase is WindowPhase.Open or WindowPhase.None)
            {
                window.PumpActive = true;
                pumpOwner = new object();
                window.PumpOwner = pumpOwner;
                startPump = true;
            }
        }

        if (superseded is not null && !ReferenceEquals(superseded, intent))
        {
            CompleteIntent(superseded, result: false);
        }

        if (startPump)
        {
            StartPump(window, intent.RegionName, pumpOwner!);
        }
    }

    /// <summary>
    /// Awaits the intent's fate for a caller outside any lifecycle callback.
    /// If the caller cancels first, the intent is withdrawn when it is still
    /// pending; an intent already executing runs its hop to completion.
    /// </summary>
    private async Task<bool> AwaitIntentFateAsync(NavigationIntent intent)
    {
        try
        {
            return await intent.Fate!.Task.WaitAsync(intent.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (intent.CancellationToken.IsCancellationRequested)
        {
            WithdrawIntent(intent);
            // When the intent was already executing, withdrawal no longer
            // applies and the pump still completes its fate afterwards —
            // possibly faulted — with nobody left awaiting it. The eventual
            // fault is REPORTED, not merely observed: FaultOrReport diverts
            // to SubscriberError when it sees the intent already withdrawn,
            // but a fault that was delivered into the fate just before the
            // cancellation landed (the check and the fault are not atomic)
            // would otherwise sit in this abandoned fate and be silently
            // dropped. Reporting it here closes that gap — and it cannot
            // double-report, because when FaultOrReport does take the
            // report branch the fate is never faulted, so this continuation
            // never runs.
            _ = intent.Fate!.Task.ContinueWith(
                completed =>
                {
                    if (completed.Exception is not { } exception)
                    {
                        return;
                    }

                    // Mirror the fateless delivery in CompleteExecutedIntent:
                    // a fate faulted with several collected errors reports
                    // each one, not just the first. Cancellation artifacts
                    // among them are outcomes, not errors, and are skipped —
                    // a fate that faulted purely with an
                    // OperationCanceledException reports nothing.
                    if (exception.InnerExceptions.Count > 1)
                    {
                        foreach (var inner in exception.InnerExceptions)
                        {
                            if (FirstNonCancellationError(inner) is { } real)
                            {
                                ReportSubscriberError(real);
                            }
                        }
                    }
                    else if (exception.InnerException is { } single
                        && FirstNonCancellationError(single) is { } real)
                    {
                        ReportSubscriberError(real);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw new OperationCanceledException(intent.CancellationToken);
        }
    }

    /// <summary>
    /// Removes the intent from its region's slot when it is still pending there.
    /// An intent already taken by the pump as a departure candidate is not
    /// withdrawn here; the pump stops honoring it only until its From phase
    /// starts, after which the departure runs to completion.
    /// </summary>
    private void WithdrawIntent(NavigationIntent intent)
    {
        bool removed = false;
        using (_stateLock.EnterScope())
        {
            if (_windows.TryGetValue(intent.RegionName, out var window)
                && ReferenceEquals(window.PendingIntent, intent))
            {
                window.PendingIntent = null;
                removed = true;
            }
        }

        if (removed)
        {
            intent.Fate?.TrySetCanceled(intent.CancellationToken);
        }
    }

    // ────────────────────────── Fate completion ──────────────────────────

    private static void CompleteIntent(NavigationIntent intent, bool result)
        => intent.Fate?.TrySetResult(result);

    private static void CancelIntent(NavigationIntent intent)
        => intent.Fate?.TrySetCanceled(intent.CancellationToken);

    private static void FaultIntent(NavigationIntent intent, Exception error)
        => intent.Fate?.TrySetException(error);

    /// <summary>
    /// Delivers an internal failure to the intent that hit it: faulted into
    /// its fate when a caller is awaiting it, otherwise reported via
    /// <see cref="SubscriberError"/>. The "otherwise" covers two cases:
    /// callback-registered intents, which have no fate; and outside intents
    /// whose caller has already withdrawn (its token is cancelled — it
    /// observed OperationCanceledException from its own token and is no
    /// longer awaiting the fate). Faulting a withdrawn intent's fate would
    /// drop the exception silently: nobody awaits it. (The caller's
    /// post-withdrawal observer in AwaitIntentFateAsync is the backstop
    /// for the mirror race — a fault delivered into the fate just before
    /// the cancellation landed — and reports any fault it finds there.)
    /// Failure paths must never drop an exception silently.
    /// </summary>
    private void FaultOrReport(NavigationIntent intent, Exception error)
    {
        if (intent.Fate is not null && !intent.IsWithdrawn)
        {
            FaultIntent(intent, error);
        }
        else
        {
            ReportSubscriberError(error);
        }
    }

    /// <summary>
    /// Completes an intent that was executed: faulted with its hop's errors when
    /// the hop produced any, otherwise true. For intents without a fate
    /// (registered from a lifecycle callback) the errors are reported via
    /// <see cref="SubscriberError"/> instead.
    /// </summary>
    private void CompleteExecutedIntent(NavigationIntent intent, List<Exception> errors)
    {
        if (errors.Count == 0)
        {
            CompleteIntent(intent, result: true);
            return;
        }

        if (intent.Fate is not null)
        {
            if (errors.Count == 1)
            {
                intent.Fate.TrySetException(errors[0]);
            }
            else
            {
                intent.Fate.TrySetException(new AggregateException(errors));
            }
        }
        else
        {
            foreach (var error in errors)
            {
                ReportSubscriberError(error);
            }
        }
    }

    /// <summary>
    /// Completes and clears the region's pending intent, if any. Called on
    /// disposal: pending requests will never execute.
    /// </summary>
    private void DrainWindowIntents()
    {
        List<NavigationIntent> pending = [];
        using (_stateLock.EnterScope())
        {
            foreach (var window in _windows.Values)
            {
                if (window.PendingIntent is not null)
                {
                    pending.Add(window.PendingIntent);
                    window.PendingIntent = null;
                }

                // Wake any pump parked on an in-flight lifecycle callback:
                // after disposal no registration can complete the signal, and
                // the wait loops re-check _disposed when they wake.
                window.CloseSignal?.TrySetResult();
                window.DisposalSignal.TrySetResult();
                window.Phase = WindowPhase.None;
            }
        }

        foreach (var intent in pending)
        {
            CompleteIntent(intent, result: false);
        }
    }

    // ────────────────────────── The pump ──────────────────────────

    private void StartPump(RegionWindow window, string regionName, object owner)
    {
        _ = RunPumpGuardedAsync(window, regionName, owner);
    }

    /// <summary>
    /// Runs the region's pump loop. The loop is written to complete every fate
    /// it touches and to report step errors itself; this guard is the last
    /// resort that keeps a pump failure from wedging the region: the pump flag
    /// is released so a later registration can restart the pump.
    /// </summary>
    private async Task RunPumpGuardedAsync(RegionWindow window, string regionName, object owner)
    {
        try
        {
            await RunPumpAsync(window, regionName, owner).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportSubscriberError(ex);
        }
        finally
        {
            bool restart = false;
            object? restartOwner = null;
            NavigationIntent? leftover = null;
            bool ownsFlag = false;
            bool diedMidWindow = false;
            using (_stateLock.EnterScope())
            {
                // Only the run that still owns the flag may release or hand
                // it over. When a successor pump already took ownership (a
                // registration interposed after this run's settling step
                // released the flag), this exit path touches nothing — not
                // even the entering state, which by then belongs to the
                // successor (its own loop-top disposal check settles it).
                ownsFlag = window.PumpActive && ReferenceEquals(window.PumpOwner, owner);
                if (ownsFlag)
                {
                    window.PumpActive = false;
                    window.PumpOwner = null;
                    if (window.PendingIntent is not null)
                    {
                        if (_disposed)
                        {
                            leftover = window.PendingIntent;
                            window.PendingIntent = null;
                        }
                        else
                        {
                            restart = true;
                            restartOwner = new object();
                            window.PumpOwner = restartOwner;
                            window.PumpActive = true;
                        }
                    }
                    else if (!_disposed && window.Phase is WindowPhase.Entering or WindowPhase.Leaving)
                    {
                        // The pump is exiting (a step threw) in the middle of
                        // a window with nothing left to execute: no step will
                        // ever advance this phase again, and registrations
                        // only start pumps in the Open/None phases, so the
                        // region would wedge permanently. Normalize the phase
                        // to the resting state the stack actually shows, so
                        // the next registration can start a fresh pump.
                        diedMidWindow = true;
                        window.Phase = _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                            ? WindowPhase.Open
                            : WindowPhase.None;
                        window.ChainDepartures = 0;
                        window.ChainTrace.Clear();
                    }

                    if (!restart && leftover is null && !_disposed)
                    {
                        RemoveWindowIfIdleLocked(window, regionName);
                    }
                }
            }

            if (_disposed && ownsFlag)
            {
                AbandonPendingEntering(window, owner);
            }

            if (diedMidWindow)
            {
                // Settle any entering state this run left unconsumed (its
                // fate and gate), so the normalized region starts clean.
                AbandonPendingEntering(window, owner);
            }

            if (leftover is not null)
            {
                CompleteIntent(leftover, result: false);
            }

            if (restart)
            {
                StartPump(window, regionName, restartOwner!);
            }
        }
    }

    /// <summary>
    /// The per-region pump loop. Each iteration handles one phase step; the loop
    /// exits when the region settles (a page rests with no pending intent) or
    /// when the service is disposed.
    /// </summary>
    private async Task RunPumpAsync(RegionWindow window, string regionName, object owner)
    {
        while (true)
        {
            if (_disposed)
            {
                // A hop may have landed and stored its entering state after
                // the last iteration: the entering step will never run, so
                // settle that state here instead of leaking the caller's fate
                // and the entry's in-flight gate registration.
                AbandonPendingEntering(window, owner);
                return;
            }

            WindowPhase phase;
            using (_stateLock.EnterScope())
            {
                phase = window.Phase;
            }

            bool keepGoing = phase switch
            {
                WindowPhase.Entering => await RunEnteringStepAsync(window, regionName).ConfigureAwait(false),
                WindowPhase.Open or WindowPhase.None => await RunOpenStepAsync(window, regionName).ConfigureAwait(false),
                // Leaving never parks the loop: a departure runs to completion
                // inside the step that started it. Finding it here means a
                // departure ended abnormally — recover instead of spinning.
                _ => await RecoverStuckLeaving(window, regionName).ConfigureAwait(false),
            };

            if (!keepGoing)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Settles entering state that a hop stored but the pump will never
    /// consume (the service was disposed in between): exits the entry's
    /// commit-time in-flight registration and completes the landed intent as
    /// discarded. Called only on the owning pump's flow: when a successor
    /// pump has already taken ownership of the window (its token is in
    /// <see cref="RegionWindow.PumpOwner"/>), the entering state belongs to
    /// the successor — its own loop-top disposal check settles it — and this
    /// call leaves it untouched.
    /// </summary>
    private void AbandonPendingEntering(RegionWindow window, object owner)
    {
        NavigationEntry? entry;
        Task? gate;
        NavigationIntent? intent;
        using (_stateLock.EnterScope())
        {
            if (window.PumpOwner is not null && !ReferenceEquals(window.PumpOwner, owner))
            {
                return;
            }

            entry = window.EnteringEntry;
            gate = window.EnteringGate;
            intent = window.EnteredIntent;
            window.EnteringEntry = null;
            window.EnteringGate = null;
            window.EnteredIntent = null;
            window.EnteredErrors = null;
            window.EnteredWork = null;
        }

        if (entry is not null && gate is not null)
        {
            entry.ExitInFlightLifecycle(gate);
        }

        if (intent is not null)
        {
            CompleteIntent(intent, result: false);
        }
    }

    /// <summary>
    /// Fires the queued Navigation PropertyChanged notifications of a hop
    /// whose normal completion path (the entering step) will not run — the
    /// hop-failure recovery branches. A detached page whose notification is
    /// dropped here would keep its host-side Navigation reference looking
    /// attached forever.
    /// </summary>
    private static void RaiseNavigationChangedNotifications(TransitionWork work, List<Exception> errors)
    {
        foreach (var vm in work.NavigationChangedNotifications)
        {
            try { RaiseNavigationChangedNotification(vm); }
            catch (Exception ex) { CollectError(errors, ex); }
        }
    }

    /// <summary>
    /// Registers a recovery activation gate for a stack-top entry whose hop
    /// threw after committing it, ordered against disposal: the registration
    /// happens under the navigation lock with the disposed flag re-validated
    /// inside, so either a disposal snapshot observes the gate, or the
    /// recovery abstains. Returns null when the service is disposed.
    /// </summary>
    private async Task<Task?> RegisterRecoveryGateAsync(NavigationEntry entry)
    {
        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return _disposed ? null : entry.EnterInFlightLifecycle();
        }
        finally
        {
            _navigationLock.Release();
        }
    }

    private Task<bool> RecoverStuckLeaving(RegionWindow window, string regionName)
    {
        ReportSubscriberError(new InvalidOperationException(
            $"Internal navigation state for region '{regionName}' was found in the Leaving phase " +
            "outside a departure; the region was rested on its current page."));

        using (_stateLock.EnterScope())
        {
            window.Phase = WindowPhase.Open;
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Open/None step: when no intent is pending the region has settled and the
    /// pump exits. With no current page, a Navigate intent executes directly
    /// (there is no window to close); a GoBack intent simply fails. With a
    /// current page, the pending intent starts a departure.
    /// </summary>
    /// <returns>False when the pump should exit (settled or disposed).</returns>
    private async Task<bool> RunOpenStepAsync(RegionWindow window, string regionName)
    {
        NavigationIntent? intent;
        WindowPhase phase;
        INavigationViewModel? settleRaise = null;
        using (_stateLock.EnterScope())
        {
            phase = window.Phase;
            intent = window.PendingIntent;
            window.PendingIntent = null;
            if (intent is null)
            {
                // Settled. Releasing the pump flag (and its ownership token)
                // in the same critical section as the empty-slot read keeps
                // this atomic against RegisterIntent: a later registration
                // observes PumpActive == false and starts a successor pump
                // under its own token.
                window.PumpActive = false;
                window.PumpOwner = null;
                window.ChainDepartures = 0;
                window.ChainTrace.Clear();
                RemoveWindowIfIdleLocked(window, regionName);
                if (phase == WindowPhase.Open
                    && !window.CurrentPageSurfaced
                    && _regionStacks.TryGetValue(regionName, out var stack)
                    && stack.Count > 0)
                {
                    // Last-resort surfacing: every earlier raise point defers
                    // to a later one when a further intent is pending (the
                    // entering step, refresh/revive rests checks, the
                    // departure wrapper); if that intent was withdrawn before
                    // executing, this settle is the page's final chance to be
                    // reported as the region's resting current page.
                    window.CurrentPageSurfaced = true;
                    settleRaise = stack.Peek().ViewModel;
                }
            }
        }

        if (intent is null)
        {
            if (settleRaise is not null && !_disposed)
            {
                RaiseRegionNavigated(regionName, settleRaise);
            }

            return false;
        }

        if (intent.IsWithdrawn)
        {
            CancelIntent(intent);
            return true;
        }

        if (phase == WindowPhase.None)
        {
            if (intent.Kind == NavigationIntentKind.GoBack)
            {
                CompleteIntent(intent, result: false);
                return true;
            }

            var work = new TransitionWork();
            var errors = new List<Exception>();
            HopResult result;
            try
            {
                result = await intent.ExecuteHop!(intent, window, work, errors).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The same containment as the departure path: a commit throw
                // must not leave the caller's fate incomplete — or a pushed
                // entry's gate without an owner — just because the region had
                // no departing page.
                FaultOrReport(intent, ex);
                await RunDisposalActionsAsync(work).ConfigureAwait(false);
                errors.AddRange(work.Errors);

                NavigationEntry? top;
                using (_stateLock.EnterScope())
                {
                    top = _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                        ? stack.Peek()
                        : null;
                }

                if (top is not null)
                {
                    var recoveryGate = await RegisterRecoveryGateAsync(top).ConfigureAwait(false);
                    if (recoveryGate is null)
                    {
                        return false;
                    }

                    SetEntering(window, new HopResult(top, recoveryGate, work), intent: null!, errors);
                }
                else
                {
                    RaiseEvents(work.Events, ReportSubscriberError);
                    foreach (var disposalError in work.Errors)
                    {
                        ReportSubscriberError(disposalError);
                    }
                }

                return !_disposed;
            }

            if (result.Entry is not null)
            {
                SetEntering(window, result, intent, errors);
            }

            return true;
        }

        return await RunDepartureAsync(window, regionName, intent).ConfigureAwait(false);
    }

    /// <summary>
    /// Entering step: runs the entering page's To callback, completes the fate
    /// of the intent whose hop landed here, then either lets the page rest
    /// (raising <see cref="RegionNavigated"/> — the page is the region's resting
    /// current page) or, when a further intent is already pending, starts the
    /// page's departure without ever surfacing it.
    /// </summary>
    /// <returns>False when the pump should exit (disposed).</returns>
    private async Task<bool> RunEnteringStepAsync(RegionWindow window, string regionName)
    {
        var entry = window.EnteringEntry;
        var gate = window.EnteringGate;
        var intent = window.EnteredIntent;
        var errors = window.EnteredErrors ?? [];
        var work = window.EnteredWork;
        window.EnteringEntry = null;
        window.EnteringGate = null;
        window.EnteredIntent = null;
        window.EnteredErrors = null;
        window.EnteredWork = null;

        if (entry is null || gate is null)
        {
            // Defensive: entering state is always set as a unit by a hop.
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            return true;
        }

        var toErrors = new List<Exception>();
        var toTask = RunToCallbacksAsync(entry, entry.Parameter, gate, toErrors);
        NavigationIntent? enteringClaimedIntent = null;
        if (!toTask.IsCompleted)
        {
            // Close-request interrupt: a request registered while this page is
            // still activating wants the window closed. The page's lifecycle
            // token is the documented "you are leaving" signal — cancelling it
            // lets a well-behaved activation stop promptly. A callback that
            // ignores the token is given the disposal-gate window and then
            // abandoned (it keeps running in the background); the window
            // closes anyway, and the gate timeout is reported exactly once.
            //
            // A wake is only live when a pending intent actually survives: the
            // request that completed the signal may since have been withdrawn,
            // in which case the interrupt is void — the signal is re-armed for
            // the next registration and the activation remains authoritative.
            // Falling through with the activation still in flight would raise
            // RegionNavigated early and read the runner's error list while the
            // runner is still appending to it.
            //
            // The wake is judged and claimed in one critical section, before
            // anything is cancelled: a withdrawn or absent intent voids the
            // wake; a self no-op intent is consumed in place (its fate
            // completes true) without touching the activation token — the page
            // is not leaving, so its activation must not observe a
            // cancellation; any other intent is claimed out of the slot and
            // becomes this window's committed departure candidate. Only a
            // claimed candidate cancels the token: from the claim on, slot
            // withdrawal can no longer remove it, so a cancellation of the
            // page can never be left behind by a request that went away.
            NavigationIntent? claimedIntent = null;
            while (!toTask.IsCompleted)
            {
                Task? signal;
                using (_stateLock.EnterScope())
                {
                    signal = window.CloseSignal?.Task;
                }

                if (signal is null)
                {
                    await toTask.ConfigureAwait(false);
                    break;
                }

                var first = await Task.WhenAny(toTask, signal).ConfigureAwait(false);
                if (ReferenceEquals(first, toTask) || toTask.IsCompleted)
                {
                    break;
                }

                // Re-arm before judging this wake, so a registration racing
                // the liveness check is never lost: it either lands in the
                // slot before the check (and is seen) or completes the fresh
                // signal (and wakes the next iteration).
                using (_stateLock.EnterScope())
                {
                    if (ReferenceEquals(window.CloseSignal?.Task, signal))
                    {
                        window.CloseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                }

                if (_disposed)
                {
                    break;
                }

                NavigationIntent? consumedNoOp = null;
                using (_stateLock.EnterScope())
                {
                    var pending = window.PendingIntent;
                    if (pending is { IsWithdrawn: false })
                    {
                        window.PendingIntent = null;
                        if (IsSelfNoOp(pending, entry))
                        {
                            consumedNoOp = pending;
                        }
                        else
                        {
                            claimedIntent = pending;
                        }
                    }
                }

                if (consumedNoOp is not null)
                {
                    CompleteIntent(consumedNoOp, result: true);
                    continue;
                }

                if (claimedIntent is null)
                {
                    continue;
                }

                NavigationEntry.CancelOutsideLocks(entry.LifecycleCts);
                var finished = await Task.WhenAny(toTask, Task.Delay(DisposalGateTimeout)).ConfigureAwait(false);
                if (!ReferenceEquals(finished, toTask))
                {
                    entry.AbandonedGateTask = gate;
                    ReportGateTimeout(entry);
                }

                break;
            }

            // A claimed departure candidate is run by the departure step
            // below; on the disposal exit its fate is completed there.
            enteringClaimedIntent = claimedIntent;
        }

        if (toTask.IsCompleted)
        {
            // The runner collects callback exceptions into toErrors and never
            // faults; awaiting a completed task only joins the flow.
            await toTask.ConfigureAwait(false);
        }

        // Snapshot under the list's lock: after a gate-timeout abandonment the
        // runner may still be appending to toErrors in the background.
        lock (toErrors)
        {
            errors.AddRange(toErrors);
        }

        // PropertyChanged notifications for the hop's attach/detach, then the
        // hop's queued events (ViewModelReleased), mirroring the historical
        // per-transition ordering: To callbacks, notifications, events.
        if (work is not null)
        {
            foreach (var vm in work.NavigationChangedNotifications)
            {
                try { RaiseNavigationChangedNotification(vm); }
                catch (Exception ex) { errors.Add(ex); }
            }

            RaiseEvents(work.Events, ReportSubscriberError);
        }

        // Completes the entered intent with its hop's outcome. When the page
        // comes to rest, RegionNavigated is raised BEFORE this runs — an
        // awaiting caller must observe subscribers as already notified, the
        // same order the in-place refresh path uses.
        void CompleteEnteredIntent()
        {
            if (intent is not null)
            {
                CompleteExecutedIntent(intent, errors);
            }
            else if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    ReportSubscriberError(error);
                }
            }
        }

        if (_disposed)
        {
            if (enteringClaimedIntent is not null)
            {
                CompleteIntent(enteringClaimedIntent, result: false);
            }

            CompleteEnteredIntent();
            return false;
        }

        if (enteringClaimedIntent is not null)
        {
            CompleteEnteredIntent();
            return await RunDepartureAsync(window, regionName, enteringClaimedIntent).ConfigureAwait(false);
        }

        bool hasPending;
        using (_stateLock.EnterScope())
        {
            hasPending = window.PendingIntent is not null;
            if (!hasPending)
            {
                window.Phase = WindowPhase.Open;
            }
        }

        if (!hasPending)
        {
            // The page rests: this is the only point at which RegionNavigated is
            // raised, so intermediate pages of a redirect chain never surface.
            RaiseRegionNavigatedTracked(window, regionName, entry.ViewModel);
            CompleteEnteredIntent();
            return true;
        }

        // A further request is already pending: the window closes right away.
        NavigationIntent? next;
        using (_stateLock.EnterScope())
        {
            next = window.PendingIntent;
            window.PendingIntent = null;
        }

        if (next is null)
        {
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            RaiseRegionNavigatedTracked(window, regionName, entry.ViewModel);
            CompleteEnteredIntent();
            return true;
        }

        if (next.IsWithdrawn)
        {
            CancelIntent(next);
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            RaiseRegionNavigatedTracked(window, regionName, entry.ViewModel);
            CompleteEnteredIntent();
            return true;
        }

        CompleteEnteredIntent();
        return await RunDepartureAsync(window, regionName, next).ConfigureAwait(false);
    }

    private void SetEntering(RegionWindow window, HopResult result, NavigationIntent intent, List<Exception> errors)
    {
        window.EnteringEntry = result.Entry;
        window.EnteringGate = result.ToGate;
        window.EnteredIntent = intent;
        window.EnteredErrors = errors;
        window.EnteredWork = result.Work;
        using (_stateLock.EnterScope())
        {
            window.Phase = WindowPhase.Entering;
            window.CloseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.CurrentPageSurfaced = false;
        }
    }

    /// <summary>
    /// Raises <see cref="INavigationService.RegionNavigated"/> for a page that
    /// is coming to rest as the region's current page and records it as
    /// surfaced, so a later refused departure for the same page does not
    /// raise it a second time.
    /// </summary>
    private void RaiseRegionNavigatedTracked(RegionWindow window, string regionName, INavigationViewModel viewModel)
    {
        using (_stateLock.EnterScope())
        {
            window.CurrentPageSurfaced = true;
        }

        RaiseRegionNavigated(regionName, viewModel);
    }

    // ────────────────────────── Departure ──────────────────────────

    /// <summary>
    /// Runs one departure of the region's current page for the given initial
    /// candidate intent: chain-limit check, leave guard, From callbacks, final
    /// sampling of the intent slot, and execution of the winning intent.
    /// Afterwards it guarantees the surfacing invariant: if the departure was
    /// refused (or its hop failed and the departing page was revived) and the
    /// region's current page has never been surfaced since it landed — the
    /// case for departures chained straight from the entering step, where the
    /// landing raise was deferred — the page is surfaced now that it comes to
    /// rest. Without this, hosts would keep showing the previous page while
    /// the refused page is already current.
    /// </summary>
    /// <returns>False when the pump should exit (disposed).</returns>
    private async Task<bool> RunDepartureAsync(RegionWindow window, string regionName, NavigationIntent candidate)
    {
        var continues = await RunDepartureCoreAsync(window, regionName, candidate).ConfigureAwait(false);
        if (continues && !_disposed)
        {
            SurfaceCurrentPageIfNeeded(window, regionName);
        }

        return continues;
    }

    private void SurfaceCurrentPageIfNeeded(RegionWindow window, string regionName)
    {
        INavigationViewModel? toRaise = null;
        using (_stateLock.EnterScope())
        {
            if (window.Phase == WindowPhase.Leaving)
            {
                // A departure path ended without landing a new page and
                // without reaching an explicit rest state (defensive hop
                // exits): rest the region on whatever is on top instead of
                // leaving the phase parked at Leaving.
                window.Phase = WindowPhase.Open;
            }

            if (window.Phase == WindowPhase.Open
                && !window.CurrentPageSurfaced
                && window.PendingIntent is null
                && _regionStacks.TryGetValue(regionName, out var stack)
                && stack.Count > 0)
            {
                // The slot must be empty — the same "rests" test the refresh
                // and revive paths apply. A core step that ended with a
                // further intent still pending (a refresh whose To registered
                // the next request, a revive with a follow-up queued) leaves
                // the page about to depart again; surfacing it here would
                // report a resting page that is already leaving.
                window.CurrentPageSurfaced = true;
                toRaise = stack.Peek().ViewModel;
            }
        }

        if (toRaise is not null)
        {
            RaiseRegionNavigated(regionName, toRaise);
        }
    }

    /// <summary>
    /// Repairs the activation of a page that is staying after all: when the
    /// departure was chained from an interrupted entering step, the page's
    /// lifecycle source was already cancelled to close its window. Renew it
    /// so the page's later callbacks receive a live token. (A token the page
    /// itself captured stays cancelled — cancellation cannot be undone.)
    /// For departures that start from a resting page the source has not been
    /// cancelled when the page-stays exits run, so this is a no-op there.
    /// </summary>
    private void RenewActivationIfCancelled(NavigationEntry entry)
    {
        if (_disposed || !entry.LifecycleCts.IsCancellationRequested)
        {
            return;
        }

        var retired = entry.BeginNewActivation();
        entry.CancelRetiredOutsideLocks(retired);

        // A disposal that collected the entry's sources while the renewal
        // was swapping must not leave the fresh source live: cancel it too.
        // (The entry is dead in that interleaving; no callback runs under
        // the fresh source, and it is left to the GC like any source that
        // teardown did not collect.)
        if (_disposed)
        {
            NavigationEntry.CancelOutsideLocks(entry.LifecycleCts);
        }
    }

    /// <summary>
    /// True when an exception carries nothing but cancellation: a bare
    /// <see cref="OperationCanceledException"/> whose inner-exception chain
    /// is empty or consists only of further
    /// <see cref="OperationCanceledException"/>s, or an aggregate whose
    /// flattened leaves all satisfy the same rule. An
    /// <see cref="OperationCanceledException"/> wrapping a real error (for
    /// example <c>new OperationCanceledException("x", realError)</c>) is
    /// NOT cancellation-only: the wrapped error must be reported, not
    /// swallowed as a cancellation outcome.
    /// </summary>
    private static bool IsCancellationOnly(Exception error)
    {
        if (error is AggregateException aggregate)
        {
            // Not vacuously: an aggregate with no leaves at all (a guard
            // threw `new AggregateException()`) is a failure, not
            // cancellation — "all leaves are OCE" must not hold for zero
            // leaves.
            var any = false;
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                any = true;
                if (FirstNonCancellationError(inner) is not null)
                {
                    return false;
                }
            }

            return any;
        }

        return error is OperationCanceledException && FirstNonCancellationError(error) is null;
    }

    /// <summary>
    /// Walks an <see cref="OperationCanceledException"/> inner-exception
    /// chain and returns the first error that is not itself cancellation,
    /// or <see langword="null"/> when the chain carries nothing but
    /// cancellation. A non-cancellation exception is returned as-is.
    /// Callers use this wherever a fault is filtered with "skip
    /// cancellation artifacts": skipping must stop at a wrapped real error
    /// instead of discarding it along with its wrapper.
    /// </summary>
    private static Exception? FirstNonCancellationError(Exception error)
    {
        var current = error;
        while (current is OperationCanceledException oce && oce.InnerException is not null)
        {
            current = oce.InnerException;
        }

        return current is OperationCanceledException ? null : current;
    }

    /// <summary>
    /// Reports a candidate's deferred classification failures (see
    /// <see cref="RunDepartureCoreAsync"/>) through
    /// <see cref="INavigationService.SubscriberError"/>.
    /// </summary>
    private void ReportClassificationErrors(List<Exception>? errors)
    {
        if (errors is null)
        {
            return;
        }

        foreach (var ex in errors)
        {
            ReportSubscriberError(ex);
        }
    }

    private async Task<bool> RunDepartureCoreAsync(RegionWindow window, string regionName, NavigationIntent candidate)
    {
        NavigationEntry? current;
        using (_stateLock.EnterScope())
        {
            current = _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                ? stack.Peek()
                : null;
        }

        if (current is null)
        {
            // The page vanished (service disposed concurrently): nothing to leave.
            CompleteIntent(candidate, result: false);
            return !_disposed;
        }

        // Self-targeting requests never start a departure. The window closes
        // here (the page rests), so an Entering phase left over from a
        // landing-chained candidate is settled to Open.
        if (IsSelfNoOp(candidate, current))
        {
            CompleteIntent(candidate, result: true);
            RenewActivationIfCancelled(current);
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            return true;
        }

        if (candidate.Kind == NavigationIntentKind.GoBack && !CanGoBack(regionName))
        {
            CompleteIntent(candidate, result: false);
            RenewActivationIfCancelled(current);
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            return true;
        }

        // Classification failures of a GoBackTo candidate whose departure
        // proceeds: held back (not reported at the search sites) because the
        // executing hop classifies again and reports through its error list.
        // Every terminal below where the candidate's hop never runs — guard
        // veto/fault/cancel, withdrawal, chain limit, supersession at final
        // sampling — flushes them instead, so each failure is reported
        // exactly once on every route.
        List<Exception>? candidateClassificationErrors = null;

        if (candidate.Kind == NavigationIntentKind.GoBackTo)
        {
            // A back-to request whose target is not on the stack — or already
            // is the active page — never starts a departure, exactly like a
            // back request at the bottom of the stack: no guard runs and the
            // current page is not disturbed. The executing hop re-validates
            // the target, so this early verdict only needs the snapshot.
            List<NavigationEntry> snapshot;
            using (_stateLock.EnterScope())
            {
                snapshot = _regionStacks.TryGetValue(regionName, out var s) ? [.. s] : [];
            }

            var classificationErrors = new List<Exception>();
            var target = FindGoBackToTarget(snapshot, candidate, classificationErrors);
            candidateClassificationErrors = classificationErrors;
            if (target is null || ReferenceEquals(target, current))
            {
                ReportClassificationErrors(candidateClassificationErrors);
                candidateClassificationErrors = null;

                CompleteIntent(candidate, result: false);
                RenewActivationIfCancelled(current);
                using (_stateLock.EnterScope())
                {
                    window.Phase = WindowPhase.Open;
                }

                return true;
            }
        }

        // Leave guard, with the candidate that started this departure as its
        // context. A later overwrite of the candidate does not re-run the guard.
        // The guard runs as part of the current entry's lifecycle processing:
        // the lifecycle-entry marker is set for its duration, so navigation
        // calls issued from within the guard register into the window's slot
        // (and return immediately) instead of awaiting a fate that can only
        // complete after this very guard returns — that wait would deadlock.
        var guardContext = BuildGuardContext(regionName, candidate, current);
        GuardOutcome guardOutcome;
        var previousCurrent = _currentLifecycleEntry.Value;
        _currentLifecycleEntry.Value = current;
        try
        {
            guardOutcome = await RunGuardAsync(current.ViewModel, guardContext, candidate.CancellationToken, window.DisposalSignal.Task).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCancellationOnly(ex))
        {
            // The guard itself ended in cancellation — thrown directly (or
            // carried as an aggregate's sole content) instead of surfacing
            // as a Canceled task, so the race in RunGuardAsync could not
            // deliver it as the Cancelled outcome. Cancellation is control
            // flow, not an error: the departure is aborted exactly as for
            // GuardOutcome.Cancelled (including the disposal check that
            // precedes it) and nothing is reported, even when the candidate
            // was already withdrawn (where FaultOrReport would divert the
            // exception to SubscriberError).
            if (_disposed)
            {
                CompleteIntent(candidate, result: false);
                return false;
            }

            CancelIntent(candidate);
            ReportClassificationErrors(candidateClassificationErrors);
            RenewActivationIfCancelled(current);
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            return true;
        }
        catch (Exception ex)
        {
            // An aggregate failure is delivered by its real leaves only, as
            // ReportGuardFault does — cancellation artifacts inside it are
            // outcomes, not errors. A candidate with no fate, or already
            // withdrawn, reports each real leaf to SubscriberError; a
            // candidate still awaited by its caller faults with the single
            // real leaf, or an aggregate of them. (An aggregate with no
            // leaves has nothing to classify and goes through FaultOrReport
            // with the original exception.)
            var leavesReported = false;
            if (ex is AggregateException aggregate)
            {
                var reals = new List<Exception>();
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    if (FirstNonCancellationError(inner) is { } real)
                    {
                        reals.Add(real);
                    }
                }

                if (candidate.Fate is null || candidate.IsWithdrawn)
                {
                    foreach (var real in reals)
                    {
                        ReportSubscriberError(real);
                        leavesReported = true;
                    }
                }
                else if (reals.Count > 0)
                {
                    FaultOrReport(candidate, reals.Count == 1 ? reals[0] : new AggregateException(reals));
                    leavesReported = true;
                }
            }

            if (!leavesReported)
            {
                // A non-aggregate guard failure may still be a cancellation
                // wrapper around a real error: deliver the wrapped error,
                // not the wrapper, to the caller (or SubscriberError).
                FaultOrReport(candidate, FirstNonCancellationError(ex) ?? ex);
            }

            ReportClassificationErrors(candidateClassificationErrors);
            RenewActivationIfCancelled(current);
            EndChain(window);
            DrainSlot(window, result: false);
            return true;
        }
        finally
        {
            _currentLifecycleEntry.Value = previousCurrent;
        }

        if (_disposed)
        {
            CompleteIntent(candidate, result: false);
            return false;
        }

        if (guardOutcome == GuardOutcome.Cancelled)
        {
            CancelIntent(candidate);
            ReportClassificationErrors(candidateClassificationErrors);
            RenewActivationIfCancelled(current);
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            return true;
        }

        if (guardOutcome == GuardOutcome.Vetoed)
        {
            // The page refuses to leave: every pending leave request is moot.
            CompleteIntent(candidate, result: false);
            ReportClassificationErrors(candidateClassificationErrors);
            RenewActivationIfCancelled(current);
            EndChain(window);
            DrainSlot(window, result: false);
            return true;
        }

        if (candidate.IsWithdrawn)
        {
            // Withdrawn before its From phase started: the departure is aborted
            // and the page was never run through From. Pending intents stand.
            CancelIntent(candidate);
            ReportClassificationErrors(candidateClassificationErrors);
            RenewActivationIfCancelled(current);
            using (_stateLock.EnterScope())
            {
                window.Phase = WindowPhase.Open;
            }

            return true;
        }

        // Chain limit: only hops driven by callback-registered requests count —
        // they are the ones that can perpetuate themselves without any external
        // actor in the loop. A hop whose candidate came from outside code
        // restarts the accounting instead. The hop that would exceed the budget
        // never starts, so the current page is not run through From (or through
        // a refresh's To) and stays intact. The judgment sits after the guard:
        // the guard is consulted for the tripping hop as for any other, a
        // vetoed hop neither consumes nor resets the budget, and a live
        // outside request that arrives while the guard runs is still in the
        // slot for the rescue below.
        int maxChain = _options.MaxNavigationChainLength;
        bool overLimit;
        using (_stateLock.EnterScope())
        {
            if (!candidate.RegisteredInCallback)
            {
                window.ChainDepartures = 0;
                window.ChainTrace.Clear();
            }

            overLimit = candidate.RegisteredInCallback && window.ChainDepartures >= maxChain;
            if (!overLimit)
            {
                if (candidate.RegisteredInCallback)
                {
                    window.ChainDepartures++;
                    window.ChainTrace.Add(candidate.Describe());
                }

            }
        }

        if (overLimit)
        {
            string trace;
            using (_stateLock.EnterScope())
            {
                trace = string.Join(" → ", window.ChainTrace);
                window.ChainDepartures = 0;
                window.ChainTrace.Clear();
                window.Phase = WindowPhase.Open;
            }

            var limitError = new InvalidOperationException(
                $"Navigation chain exceeded the maximum of {maxChain} chained departures " +
                $"(probable redirect loop). Chain: {trace} → {candidate.Describe()}. " +
                $"The current page remains active. Raise NavigationOptions.MaxNavigationChainLength " +
                $"only for legitimately deep redirect chains.");
            // Only callback-registered candidates can reach the limit (a
            // candidate with an awaiting caller resets the accounting above),
            // and such requests have no fate to fault: a runaway redirect loop
            // is a programming error and is surfaced through SubscriberError.
            ReportSubscriberError(limitError);

            // A live outside request sitting in the slot (registered during
            // the guard) is not collateral of the loop that was just stopped:
            // it stays pending, and the pump's next open step starts a fresh
            // departure for it with the accounting already reset. Anything
            // else in the slot is drained with the stopped chain. The decision
            // and the take happen in one critical section: deciding on a peek
            // and draining afterwards would let a registration supersede the
            // peeked intent in between and get the NEW intent drained instead.
            NavigationIntent? toDrain = null;
            using (_stateLock.EnterScope())
            {
                var pending = window.PendingIntent;
                if (pending is not null
                    && (pending.RegisteredInCallback || pending.IsWithdrawn))
                {
                    toDrain = pending;
                    window.PendingIntent = null;
                }
            }

            if (toDrain is not null)
            {
                CompleteIntent(toDrain, result: false);
            }

            ReportClassificationErrors(candidateClassificationErrors);
            RenewActivationIfCancelled(current);
            return true;
        }

        // In-place refresh: the guard above has been consulted (it may veto a
        // refresh, as with any departure candidate); a plain same-type no-op
        // was already short-circuited before the guard. The refresh runs the
        // page's To again without a departure.
        if (IsSelfRefresh(candidate, current))
        {
            await ExecuteRefreshAsync(window, regionName, current, candidate).ConfigureAwait(false);
            return !_disposed;
        }

        // The departure begins here: the window enters the Leaving phase and
        // requests registered during From race the disposal-gate clock.
        using (_stateLock.EnterScope())
        {
            window.Phase = WindowPhase.Leaving;
            window.CloseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // From phase. The entry's Navigation reference stays attached throughout
        // (it is detached by the hop's commit), so From callbacks can register
        // the redirect that supersedes this departure's candidate.
        // The callback is registered as in-flight under the navigation lock
        // BEFORE it starts — the same ordering the To registration gets at hop
        // commit — so a disposal snapshot taken at any moment from here on
        // observes the running From instead of releasing its scope mid-flight.
        // The disposed flag is re-validated INSIDE the lock section: disposal
        // collects in-flight gates under the same lock, so either this
        // registration is visible to the disposal snapshot, or the departure
        // abstains because disposal already ran. Checking only before the
        // lock would let a disposal that interposes between the check and
        // the registration take the page's scope while its From then runs
        // against it unobserved.
        Task? fromGate;
        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            fromGate = _disposed ? null : current.EnterInFlightLifecycle();
        }
        finally
        {
            _navigationLock.Release();
        }

        if (fromGate is null)
        {
            CompleteIntent(candidate, result: false);
            return false;
        }

        NavigationEntry.CancelOutsideLocks(current.LifecycleCts);
        var departureErrors = new List<Exception>();
        var fromTask = RunFromCallbacksAsync(current, fromGate, departureErrors);
        if (!fromTask.IsCompleted)
        {
            // A superseding request arriving while From is still in flight
            // starts the disposal-gate clock: the token is already cancelled,
            // so a From that ignores it gets the gate window and is then
            // abandoned (reported once) rather than wedging the region.
            //
            // As in the entering step, a wake counts only when a pending
            // intent survives: if the request that completed the signal was
            // withdrawn again, nobody is superseding this departure anymore —
            // the signal is re-armed and From is simply awaited, without the
            // gate clock (and its timeout report) running against it.
            while (!fromTask.IsCompleted)
            {
                Task? signal;
                using (_stateLock.EnterScope())
                {
                    signal = window.CloseSignal?.Task;
                }

                if (signal is null)
                {
                    await fromTask.ConfigureAwait(false);
                    break;
                }

                var first = await Task.WhenAny(fromTask, signal).ConfigureAwait(false);
                if (ReferenceEquals(first, fromTask) || fromTask.IsCompleted)
                {
                    break;
                }

                using (_stateLock.EnterScope())
                {
                    if (ReferenceEquals(window.CloseSignal?.Task, signal))
                    {
                        window.CloseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                }

                if (_disposed)
                {
                    break;
                }

                if (!HasLivePendingIntent(window))
                {
                    continue;
                }

                var finished = await Task.WhenAny(fromTask, Task.Delay(DisposalGateTimeout)).ConfigureAwait(false);
                if (!ReferenceEquals(finished, fromTask))
                {
                    current.AbandonedGateTask = current.InFlightGateTask;
                    ReportGateTimeout(current);
                }

                break;
            }
        }

        if (fromTask.IsCompleted)
        {
            await fromTask.ConfigureAwait(false);
        }

        // Snapshot: after a gate-timeout abandonment the From runner may still
        // be appending to departureErrors in the background; downstream steps
        // work on this settled copy.
        List<Exception> settledDepartureErrors;
        lock (departureErrors)
        {
            settledDepartureErrors = [.. departureErrors];
        }

        departureErrors = settledDepartureErrors;

        if (_disposed)
        {
            CompleteIntent(candidate, result: false);
            return false;
        }

        // Final sampling: the last intent registered up to this moment wins.
        NavigationIntent? slotIntent;
        using (_stateLock.EnterScope())
        {
            slotIntent = window.PendingIntent;
            window.PendingIntent = null;
        }

        if (slotIntent is not null && slotIntent.IsWithdrawn)
        {
            CancelIntent(slotIntent);
            slotIntent = null;
        }

        var final = slotIntent ?? candidate;
        if (!ReferenceEquals(final, candidate))
        {
            CompleteIntent(candidate, result: false);
            ReportClassificationErrors(candidateClassificationErrors);
            candidateClassificationErrors = null;

            // The chain accounting ran against the candidate before From,
            // but the hop that executes is the sampled one: reconcile the
            // count with the intent that actually drives this departure, so
            // a callback redirect sampled over an outside candidate counts
            // (and an outside request sampled over a callback candidate
            // restarts the accounting, as its own departure would have).
            using (_stateLock.EnterScope())
            {
                if (final.RegisteredInCallback && !candidate.RegisteredInCallback)
                {
                    window.ChainDepartures = 1;
                    window.ChainTrace.Clear();
                    window.ChainTrace.Add(final.Describe());
                }
                else if (!final.RegisteredInCallback && candidate.RegisteredInCallback)
                {
                    window.ChainDepartures = 0;
                    window.ChainTrace.Clear();
                }
                else if (final.RegisteredInCallback && candidate.RegisteredInCallback
                         && window.ChainTrace.Count > 0)
                {
                    window.ChainTrace[^1] = final.Describe();
                }
            }
        }

        if (IsSelfTargeting(final, current))
        {
            // The winning request targets the page that just ran From: the
            // departure is undone by re-activating the page (revive). The page
            // comes to rest again, so subscribers are notified exactly as for
            // an in-place refresh.
            await ReviveCurrentPageAsync(window, regionName, current, final, departureErrors, raiseNavigated: true).ConfigureAwait(false);
            return !_disposed;
        }

        var work = new TransitionWork();
        HopResult result;
        try
        {
            if (final.Kind == NavigationIntentKind.GoBack)
            {
                result = await ExecuteGoBackHopAsync(final, work, departureErrors, window).ConfigureAwait(false);
            }
            else
            {
                result = await final.ExecuteHop!(final, window, work, departureErrors).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // A hop settles its own expected failures (resolution, duplicates)
            // internally; anything escaping it — for example user code throwing
            // while the commit attaches navigation under the lock — must not
            // escape the pump: the region would stay in the Leaving phase
            // forever and every later caller would hang. Deliver the failure,
            // run whatever disposal work was already queued, and bring the
            // region back to a defined rest state.
            FaultOrReport(final, ex);
            await RunDisposalActionsAsync(work).ConfigureAwait(false);
            departureErrors.AddRange(work.Errors);

            NavigationEntry? top;
            using (_stateLock.EnterScope())
            {
                top = _regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 0
                    ? stack.Peek()
                    : null;
            }

            if (top is not null && ReferenceEquals(top, current) && !_disposed)
            {
                // The departure never committed: pair the already-run From
                // with a fresh To.
                RaiseNavigationChangedNotifications(work, departureErrors);
                RaiseEvents(work.Events, ReportSubscriberError);
                await ReviveCurrentPageAsync(window, regionName, current, intent: null, departureErrors).ConfigureAwait(false);
            }
            else if (top is not null && !_disposed)
            {
                // The commit landed a different page before throwing: activate
                // it (with no entered intent — its errors report via
                // SubscriberError) rather than wedging the region on an
                // unactivated stack top.
                var recoveryGate = await RegisterRecoveryGateAsync(top).ConfigureAwait(false);
                if (recoveryGate is null)
                {
                    return false;
                }

                SetEntering(window, new HopResult(top, recoveryGate, work), intent: null!, departureErrors);
            }
            else if (!_disposed)
            {
                var notificationErrors = new List<Exception>();
                RaiseNavigationChangedNotifications(work, notificationErrors);
                foreach (var notificationError in notificationErrors)
                {
                    ReportSubscriberError(notificationError);
                }

                RaiseEvents(work.Events, ReportSubscriberError);
                using (_stateLock.EnterScope())
                {
                    window.Phase = WindowPhase.None;
                }
            }

            return !_disposed;
        }

        if (result.Entry is not null)
        {
            SetEntering(window, result, final, departureErrors);
        }

        return !_disposed;
    }

    /// <summary>
    /// Whether the region's slot currently holds an intent that has not been
    /// withdrawn. Used by the From-phase interrupt: a superseding request
    /// that was registered and already withdrawn again must not start the
    /// disposal-gate clock.
    /// </summary>
    private bool HasLivePendingIntent(RegionWindow window)
    {
        using (_stateLock.EnterScope())
        {
            return window.PendingIntent is { IsWithdrawn: false };
        }
    }

    private enum GuardOutcome
    {
        Allowed,
        Vetoed,
        Cancelled,
    }

    /// <summary>
    /// Runs the leave guard, racing it against the candidate owner's
    /// cancellation. A guard abandoned by cancellation keeps running in the
    /// background; its eventual exception is observed via
    /// <see cref="SubscriberError"/> so it cannot surface as unobserved.
    /// </summary>
    private async Task<GuardOutcome> RunGuardAsync(
        INavigationViewModel viewModel,
        NavigationGuardContext context,
        CancellationToken cancellationToken,
        Task disposalSignal)
    {
        var guardTask = InvokeFromGuardAsync(viewModel, context, cancellationToken);
        // The fault-observation continuation is attached NOW, not after the
        // race: if the race is lost (caller cancellation or Dispose), the
        // abandoned guard's eventual fault must be reported, and attaching
        // late would make the report depend on this flow being rescheduled
        // first — under thread-pool load that delay is unbounded. The
        // continuation reports only when the abandon branch has marked the
        // guard abandoned; the abandon branch itself reports directly when
        // the guard has already faulted by then. The Interlocked exchange
        // keeps the report exactly-once across the two.
        var abandoned = 0;
        var reported = 0;
        void ReportGuardFault()
        {
            if (Interlocked.Exchange(ref reported, 1) == 0)
            {
                var exception = guardTask.Exception!;
                // Cancellation artifacts are outcomes, not errors — the
                // same rule AwaitIntentFateAsync applies to fate faults:
                // an inner OperationCanceledException is skipped, and a
                // guard that faulted purely with one reports nothing.
                // Anything else is reported individually (Flatten: a TCS
                // faulted with an AggregateException surfaces it as one
                // nested inner rather than as the leaf errors).
                var leaves = exception.Flatten().InnerExceptions;
                if (leaves.Count == 0)
                {
                    // An aggregate with no leaves at all (the guard threw
                    // `new AggregateException()`): there is no leaf to
                    // classify, but the failure itself must not vanish.
                    ReportSubscriberError(exception);
                    return;
                }

                foreach (var inner in leaves)
                {
                    if (FirstNonCancellationError(inner) is { } real)
                    {
                        ReportSubscriberError(real);
                    }
                }
            }
        }

        _ = guardTask.ContinueWith(
            _ =>
            {
                if (Volatile.Read(ref abandoned) == 1)
                {
                    ReportGuardFault();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // The wait also ends when the service is disposed: a guard that never
        // completes must not hang the departing caller (or the pump) past
        // Dispose. The abandoned guard task's eventual fault is observed by
        // the continuation above, as with token cancellation.
        var completed = await Task.WhenAny(guardTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken), disposalSignal).ConfigureAwait(false);
        if (!ReferenceEquals(completed, guardTask))
        {
            Volatile.Write(ref abandoned, 1);
            if (guardTask.IsFaulted)
            {
                ReportGuardFault();
            }

            return GuardOutcome.Cancelled;
        }

        return await guardTask.ConfigureAwait(false) ? GuardOutcome.Allowed : GuardOutcome.Vetoed;
    }

    /// <summary>
    /// Builds the guard context for a departure's initial candidate. For a back
    /// intent the target is the entry below the current one; the caller has
    /// already verified the region can go back.
    /// </summary>
    private NavigationGuardContext BuildGuardContext(string regionName, NavigationIntent candidate, NavigationEntry current)
    {
        if (candidate.Kind == NavigationIntentKind.GoBackTo)
        {
            List<NavigationEntry> snapshot;
            using (_stateLock.EnterScope())
            {
                snapshot = _regionStacks.TryGetValue(regionName, out var s) ? [.. s] : [];
            }

            // Classification failures are not reported here: the guard
            // context is advisory, and the executing hop runs the same
            // classification and reports through its error list.
            var classificationErrors = new List<Exception>();
            var target = FindGoBackToTarget(snapshot, candidate, classificationErrors);

            if (target is not null)
            {
                return new NavigationGuardContext(
                    regionName, target.ViewModel.GetType(), operation: null, retention: null, target.Parameter, isBack: true);
            }

            return new NavigationGuardContext(regionName, current.ViewModel.GetType(), operation: null, retention: null, current.Parameter, isBack: true);
        }

        if (candidate.Kind == NavigationIntentKind.GoBack)
        {
            using (_stateLock.EnterScope())
            {
                if (_regionStacks.TryGetValue(regionName, out var stack))
                {
                    var skipped = 0;
                    foreach (var entry in stack)
                    {
                        if (skipped++ == 1)
                        {
                            return new NavigationGuardContext(
                                regionName, entry.ViewModel.GetType(), operation: null, retention: null, entry.Parameter, isBack: true);
                        }
                    }
                }
            }

            return new NavigationGuardContext(regionName, current.ViewModel.GetType(), operation: null, retention: null, current.Parameter, isBack: true);
        }

        return new NavigationGuardContext(regionName, candidate.TargetType!, candidate.Operation, candidate.Retention, candidate.Parameter, isBack: false);
    }

    /// <summary>
    /// A default (Push + Destroy) request for the already-active type with no refresh: a no-op.
    /// It never starts a departure, whether it arrives at rest or mid-window.
    /// </summary>
    private static bool IsSelfNoOp(NavigationIntent intent, NavigationEntry current)
        => intent.Kind == NavigationIntentKind.Navigate
            && intent.Operation == NavigationOperation.Push
            && intent.Retention == NavigationRetention.Destroy
            && !intent.RefreshIfActive
            && intent.TargetType is not null
            && current.CacheKeyType == intent.TargetType;

    /// <summary>
    /// A default (Push + Destroy) request for the already-active type with refresh requested,
    /// arriving while the page rests or enters: re-activation without leaving.
    /// </summary>
    private static bool IsSelfRefresh(NavigationIntent intent, NavigationEntry current)
        => intent.Kind == NavigationIntentKind.Navigate
            && intent.Operation == NavigationOperation.Push
            && intent.Retention == NavigationRetention.Destroy
            && intent.RefreshIfActive
            && intent.TargetType is not null
            && current.CacheKeyType == intent.TargetType;

    /// <summary>
    /// A winning request that targets the current page itself (a default
    /// Push + Destroy request whose target type is the current page's type,
    /// or a refresh) after the page already ran From: the departure is undone
    /// by reviving the page. Requests with an explicit operation or retention
    /// (Replace/Reset/KeepAlive) are NOT self-targeting: they express
    /// restructuring intent and execute as regular hops.
    /// </summary>
    /// <remarks>
    /// All three self-target predicates compare the intent's target against
    /// the entry's <see cref="NavigationEntry.CacheKeyType"/> — the static
    /// type the page was navigated to — not the ViewModel's runtime type:
    /// when DI resolves a derived implementation for a base registration,
    /// a repeated navigation to the base type is still a navigation to
    /// self. This matches the KeepAlive cache, which keys entries by the
    /// same static type.
    /// </remarks>
    private static bool IsSelfTargeting(NavigationIntent intent, NavigationEntry current)
        => intent.Kind == NavigationIntentKind.Navigate
            && intent.Operation == NavigationOperation.Push
            && intent.Retention == NavigationRetention.Destroy
            && intent.TargetType is not null
            && current.CacheKeyType == intent.TargetType;

    /// <summary>
    /// Ends the current chain: the departure budget resets and the window returns
    /// to the resting phase. Pending intents are handled by the caller.
    /// </summary>
    private void EndChain(RegionWindow window)
    {
        using (_stateLock.EnterScope())
        {
            window.ChainDepartures = 0;
            window.ChainTrace.Clear();
            window.Phase = WindowPhase.Open;
        }
    }

    /// <summary>
    /// Completes and clears whatever intent is pending in the slot.
    /// </summary>
    private void DrainSlot(RegionWindow window, bool result)
    {
        NavigationIntent? pending;
        using (_stateLock.EnterScope())
        {
            pending = window.PendingIntent;
            window.PendingIntent = null;
        }

        if (pending is not null)
        {
            CompleteIntent(pending, result);
        }
    }

    // ────────────────────────── Refresh & revive ──────────────────────────

    /// <summary>
    /// Executes a refresh of the resting/entering current page: same entry, same
    /// page scope, new parameter; only the activation callbacks run again.
    /// </summary>
    private async Task ExecuteRefreshAsync(RegionWindow window, string regionName, NavigationEntry current, NavigationIntent intent)
    {
        var work = new TransitionWork();
        CancellationTokenSource? retiredCts = null;
        Task? gate = null;

        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using (_stateLock.EnterScope())
            {
                if (_regionStacks.TryGetValue(regionName, out var stack)
                    && stack.Count > 0
                    && ReferenceEquals(stack.Peek(), current))
                {
                    current.Parameter = intent.Parameter;
                    retiredCts = current.BeginNewActivation();
                    gate = current.EnterInFlightLifecycle();
                }
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        if (gate is null)
        {
            CompleteIntent(intent, result: false);
            return;
        }

        if (retiredCts is not null)
        {
            current.CancelRetiredOutsideLocks(retiredCts);
        }

        var errors = new List<Exception>();
        await RunToCallbacksAsync(current, intent.Parameter, gate, errors).ConfigureAwait(false);

        bool rests;
        using (_stateLock.EnterScope())
        {
            rests = window.PendingIntent is null
                && _regionStacks.TryGetValue(regionName, out var stack)
                && stack.Count > 0
                && ReferenceEquals(stack.Peek(), current);
            window.Phase = WindowPhase.Open;
        }

        if (rests && !_disposed)
        {
            RaiseRegionNavigatedTracked(window, regionName, current.ViewModel);
        }

        CompleteExecutedIntent(intent, errors);
    }

    /// <summary>
    /// Re-activates the current page after its From phase already ran: the
    /// departure is undone by running the page's To callback again with a fresh
    /// lifecycle token, restoring the To/From pairing. Used when the winning
    /// intent targets the current page itself, and when a hop's execution fails
    /// before any stack surgery (target construction threw, or the resolved
    /// instance already lives on another region's stack). In the failure case
    /// <paramref name="intent"/> is null — the failed intent's fate was already
    /// completed by the hop — and revive errors go to <see cref="SubscriberError"/>.
    /// </summary>
    private async Task ReviveCurrentPageAsync(
        RegionWindow window,
        string regionName,
        NavigationEntry current,
        NavigationIntent? intent,
        List<Exception> departureErrors,
        bool raiseNavigated = false)
    {
        var errors = new List<Exception>(departureErrors);
        var parameter = intent is { RefreshIfActive: true } ? intent.Parameter : current.Parameter;
        CancellationTokenSource? retiredCts = null;
        Task? gate = null;

        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using (_stateLock.EnterScope())
            {
                if (_regionStacks.TryGetValue(regionName, out var stack)
                    && stack.Count > 0
                    && ReferenceEquals(stack.Peek(), current))
                {
                    if (intent is { RefreshIfActive: true })
                    {
                        current.Parameter = intent.Parameter;
                    }

                    retiredCts = current.BeginNewActivation();
                    gate = current.EnterInFlightLifecycle();
                }
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        if (gate is null)
        {
            // The page is gone (the service is being disposed).
            if (intent is not null)
            {
                CompleteIntent(intent, result: false);
            }

            return;
        }

        if (retiredCts is not null)
        {
            current.CancelRetiredOutsideLocks(retiredCts);
        }

        await RunToCallbacksAsync(current, parameter, gate, errors).ConfigureAwait(false);

        using (_stateLock.EnterScope())
        {
            window.Phase = WindowPhase.Open;
        }

        if (raiseNavigated)
        {
            // The page rests as the region's current page again: notify before
            // completing the intent, mirroring the refresh ordering. Only when
            // it actually rests — if its revive-To registered a further
            // request, the page is already leaving again and must not surface
            // for a frame.
            bool rests;
            using (_stateLock.EnterScope())
            {
                rests = window.PendingIntent is null
                    && _regionStacks.TryGetValue(regionName, out var stack)
                    && stack.Count > 0
                    && ReferenceEquals(stack.Peek(), current);
            }

            if (rests && !_disposed)
            {
                RaiseRegionNavigatedTracked(window, regionName, current.ViewModel);
            }
        }

        if (intent is not null)
        {
            CompleteExecutedIntent(intent, errors);
        }
        else if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                ReportSubscriberError(error);
            }
        }
    }

    // ────────────────────────── Hop execution: GoBack ──────────────────────────

    /// <summary>
    /// Executes a back hop: pops the (already From-ed) current entry and
    /// re-activates the entry below it. The caller verified the region can go
    /// back before the departure started; the stack cannot have changed since,
    /// because the pump is the region's only writer.
    /// </summary>
    private async Task<HopResult> ExecuteGoBackHopAsync(NavigationIntent intent, TransitionWork work, List<Exception> errors, RegionWindow window)
    {
        var regionName = intent.RegionName;
        NavigationEntry? previous = null;
        Task? gate = null;
        CancellationTokenSource? retiredCts = null;

        await _navigationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using (_stateLock.EnterScope())
            {
                if (_regionStacks.TryGetValue(regionName, out var stack) && stack.Count > 1)
                {
                    var popped = stack.Pop();
                    previous = stack.Peek();
                    try
                    {
                        QueueOutgoingDisposal(popped, destroy: popped.Retention != NavigationRetention.KeepAlive, work);
                    }
                    catch
                    {
                        // A throwing detach must not orphan the popped
                        // entry's scope: salvage its disposal, then let the
                        // failure fault the hop as in the navigate commit.
                        SalvageOutgoingDisposal(popped, popped.Retention != NavigationRetention.KeepAlive, work);
                        throw;
                    }

                    retiredCts = previous.BeginNewActivation();
                    AttachNavigation(previous.ViewModel, work);
                    gate = previous.EnterInFlightLifecycle();
                }
            }
        }
        finally
        {
            _navigationLock.Release();
        }

        if (previous is null || gate is null)
        {
            // Defensive: cannot happen (see summary). Complete the intent and
            // revive the departing page when it is still on the stack, so its
            // already-run From is paired with a fresh To.
            CompleteIntent(intent, result: false);
            NavigationEntry? top;
            using (_stateLock.EnterScope())
            {
                top = _regionStacks.TryGetValue(regionName, out var s) && s.Count > 0 ? s.Peek() : null;
            }

            if (top is not null && !_disposed)
            {
                await ReviveCurrentPageAsync(window, regionName, top, intent: null, errors).ConfigureAwait(false);
            }

            return new HopResult(null, null, null);
        }

        if (retiredCts is not null)
        {
            previous.CancelRetiredOutsideLocks(retiredCts);
        }

        await RunDisposalActionsAsync(work).ConfigureAwait(false);
        errors.AddRange(work.Errors);

        if (_disposed)
        {
            // The entering step will never run for this hop: exit the in-flight
            // registration made at commit time, otherwise the entry's gate
            // stays open and DisposeAsync waits out the full gate timeout for
            // a callback that never ran.
            previous.ExitInFlightLifecycle(gate);
            CompleteIntent(intent, result: false);
            return new HopResult(null, null, null);
        }

        return new HopResult(previous, gate, work);
    }

    /// <summary>
    /// Executes one back-to hop: pops every entry above the intent's target
    /// and re-activates the target. Runs after the departing page's From
    /// callbacks, like the back hop; the intermediate entries run no
    /// lifecycle callbacks — they are deactivated under their own retention
    /// through the same disposal queueing as a single pop.
    /// </summary>
    /// <remarks>
    /// The target is searched twice — once before the navigation lock (the
    /// classification invokes user code, so it runs with no locks held) and
    /// re-validated by reference under the lock, because a concurrent
    /// <c>Remove</c> or disposal can change the stack in between.
    /// </remarks>
    private async Task<HopResult> ExecuteGoBackToHopAsync(NavigationIntent intent, TransitionWork work, List<Exception> errors, RegionWindow window)
    {
        var regionName = intent.RegionName;

        List<NavigationEntry> snapshot;
        using (_stateLock.EnterScope())
        {
            snapshot = _regionStacks.TryGetValue(regionName, out var s) ? [.. s] : [];
        }

        var classificationErrors = new List<Exception>();
        var target = FindGoBackToTarget(snapshot, intent, classificationErrors);
        errors.AddRange(classificationErrors);

        NavigationEntry? previous = null;
        Task? gate = null;
        CancellationTokenSource? retiredCts = null;

        if (target is not null)
        {
            await _navigationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using (_stateLock.EnterScope())
                {
                    if (_regionStacks.TryGetValue(regionName, out var stack)
                        && stack.Count > 0
                        && !ReferenceEquals(stack.Peek(), target)
                        && ContainsEntryLocked(stack, target))
                    {
                        while (!ReferenceEquals(stack.Peek(), target))
                        {
                            var popped = stack.Pop();
                            try
                            {
                                QueueOutgoingDisposal(popped, destroy: popped.Retention != NavigationRetention.KeepAlive, work);
                            }
                            catch
                            {
                                // Same salvage discipline as the back hop: a
                                // throwing detach must not orphan the popped
                                // entry's scope.
                                SalvageOutgoingDisposal(popped, popped.Retention != NavigationRetention.KeepAlive, work);
                                throw;
                            }
                        }

                        if (intent.HasParameter)
                        {
                            // The target is re-activated with the intent's
                            // parameter, which becomes its stored parameter.
                            target.Parameter = intent.Parameter;
                        }

                        previous = target;
                        retiredCts = previous.BeginNewActivation();
                        AttachNavigation(previous.ViewModel, work);
                        gate = previous.EnterInFlightLifecycle();
                    }
                }
            }
            finally
            {
                _navigationLock.Release();
            }
        }

        if (previous is null || gate is null)
        {
            // The target vanished (or became the active page) after the
            // departure phase: complete the intent and revive the departing
            // page when it is still on the stack, so its already-run From is
            // paired with a fresh To — the same recovery as the back hop.
            CompleteIntent(intent, result: false);
            NavigationEntry? top;
            using (_stateLock.EnterScope())
            {
                top = _regionStacks.TryGetValue(regionName, out var s) && s.Count > 0 ? s.Peek() : null;
            }

            if (top is not null && !_disposed)
            {
                await ReviveCurrentPageAsync(window, regionName, top, intent: null, errors).ConfigureAwait(false);
            }

            return new HopResult(null, null, null);
        }

        if (retiredCts is not null)
        {
            previous.CancelRetiredOutsideLocks(retiredCts);
        }

        await RunDisposalActionsAsync(work).ConfigureAwait(false);
        errors.AddRange(work.Errors);

        if (_disposed)
        {
            // The entering step will never run for this hop: exit the in-flight
            // registration made at commit time, otherwise the entry's gate
            // stays open and DisposeAsync waits out the full gate timeout for
            // a callback that never ran.
            previous.ExitInFlightLifecycle(gate);
            CompleteIntent(intent, result: false);
            return new HopResult(null, null, null);
        }

        return new HopResult(previous, gate, work);
    }

    /// <summary>
    /// Runs a hop's queued page-scope disposals in order, collecting exceptions
    /// into the work bucket instead of propagating them.
    /// </summary>
    private async Task RunDisposalActionsAsync(TransitionWork work)
    {
        foreach (var dispose in work.DisposeActions)
        {
            try { await dispose().ConfigureAwait(false); }
            catch (Exception ex) { work.Errors.Add(ex); }
        }
    }
}
