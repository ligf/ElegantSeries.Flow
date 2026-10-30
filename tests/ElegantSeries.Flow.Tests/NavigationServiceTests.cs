using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.Routing;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

public class TestHomeViewModel : NavigationViewModelBase
{
    public bool NavigatedToCalled { get; set; }
    public bool NavigatedFromCalled { get; set; }
}

public class ThrowingNavigatedFromViewModel : NavigationViewModelBase, INavigationAware
{
    public bool ThrowOnNavigatedFrom { get; set; }

    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom()
    {
        if (ThrowOnNavigatedFrom)
        {
            throw new InvalidOperationException("OnNavigatedFrom failed.");
        }
    }
}

public class ThrowingNavigatedToViewModel : NavigationViewModelBase, INavigationAware
{
    public void OnNavigatedTo(object? parameter)
        => throw new InvalidOperationException("OnNavigatedTo failed.");

    public void OnNavigatedFrom() { }
}

/// <summary>
/// A ViewModel that navigates again synchronously from inside
/// <see cref="INavigationAware.OnNavigatedTo"/>. If lifecycle callbacks ran under
/// the navigation lock, the nested call would deadlock.
/// </summary>
public class ReentrantNavigatedToViewModel : NavigationViewModelBase, INavigationAware
{
    public void OnNavigatedTo(object? parameter)
    {
        // Deliberately blocking: a lock held across lifecycle callbacks would deadlock here.
        NavigateToAsync<TestHomeViewModel>().GetAwaiter().GetResult();
    }

    public void OnNavigatedFrom() { }
}

/// <summary>
/// Guard that replaces itself via a nested navigation while an outer navigation is
/// awaiting this guard, superseding the request that started the departure.
/// </summary>
public class StateMismatchGuardViewModel : NavigationViewModelBase, INavigationGuard
{
    private bool _nestedDone;

    public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        if (!_nestedDone)
        {
            _nestedDone = true;
            // Register a Replace while the departure that triggered this guard
            // is in flight: the Replace is the last request in the page's window,
            // so it supersedes the pending request when the window closes.
            await Navigation!.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace);
        }

        return true;
    }
}

/// <summary>
/// Tracks disposal so tests can observe instances the service resolved but never used.
/// </summary>
public class TrackedDisposeViewModel : NavigationViewModelBase, IDisposable
{
    public static int DisposeCount;

    public void Dispose() => DisposeCount++;
}

/// <summary>Calls Remove from inside its own OnNavigatedTo and captures the outcome.</summary>
public class RemoveInCallbackViewModel : NavigationViewModelBase, INavigationAware
{
    public static Exception? Caught;

    public void OnNavigatedTo(object? parameter)
    {
        try
        {
            Navigation!.Remove<TestHomeViewModel>();
        }
        catch (Exception ex)
        {
            Caught = ex;
        }
    }

    public void OnNavigatedFrom() { }
}

public class TestDetailViewModel : NavigationViewModelBase, INavigationAware<string>, IAsyncDisposable
{
    public string? ReceivedParam { get; private set; }
    public bool Disposed { get; private set; }

    public void OnNavigatedTo(string parameter)
    {
        ReceivedParam = parameter;
    }

    public void OnNavigatedFrom() { }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

public class SimpleAwareViewModel : NavigationViewModelBase, INavigationAware
{
    public object? NavigatedToParam { get; private set; }
    public bool NavigatedToCalled { get; private set; }
    public bool NavigatedFromCalled { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        NavigatedToCalled = true;
        NavigatedToParam = parameter;
    }

    public void OnNavigatedFrom()
    {
        NavigatedFromCalled = true;
    }
}

public class CallerViewModel : NavigationViewModelBase
{
    public Task<bool> TriggerNavigateToHomeAsync() => NavigateToAsync<TestHomeViewModel>();
    public Task<bool> TriggerNavigateToDetailAsync(string param) => NavigateToAsync<TestDetailViewModel, string>(param);
    public Task<bool> TriggerGoBackAsync() => GoBackAsync();
}

public class GuardedViewModel : NavigationViewModelBase, INavigationGuard
{
    public bool AllowNavigation { get; set; } = true;

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(AllowNavigation);
    }
}

/// <summary>
/// Counts lifecycle callbacks to verify each fires exactly once.
/// </summary>
public class CountingAwareViewModel : NavigationViewModelBase, INavigationAware
{
    public int NavigatedToCount { get; private set; }
    public int NavigatedFromCount { get; private set; }

    public void OnNavigatedTo(object? parameter) => NavigatedToCount++;
    public void OnNavigatedFrom() => NavigatedFromCount++;
}

// ---- Window model test doubles ----
// Lifecycle/registration counters are static because an intermediate page's
// instance is unreachable once the chain has moved past it; each test resets
// the counters it relies on. Tests in this class run sequentially.

public class ChainLandingViewModel : NavigationViewModelBase
{
    public static int ConstructionCount;

    public ChainLandingViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;
}

public class FromLandingViewModel : NavigationViewModelBase
{
}

public class ButtonTargetViewModel : NavigationViewModelBase
{
    public static int ConstructionCount;

    public ButtonTargetViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;
}

/// <summary>Registers a navigation to the landing page from inside its own To.</summary>
public class ToRedirectViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ToCount;
    public static int FromCount;

    public static void Reset() { ToCount = 0; FromCount = 0; }

    public void OnNavigatedTo(object? parameter)
    {
        Interlocked.Increment(ref ToCount);
        _ = Navigation!.NavigateToAsync<ChainLandingViewModel>();
    }

    public void OnNavigatedFrom() => Interlocked.Increment(ref FromCount);
}

/// <summary>Registers a navigation to the landing page from inside its own From (once).</summary>
public class FromRedirectViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ToCount;
    public static int FromCount;
    private bool _redirected;

    public static void Reset() { ToCount = 0; FromCount = 0; }

    public void OnNavigatedTo(object? parameter) => Interlocked.Increment(ref ToCount);

    public void OnNavigatedFrom()
    {
        Interlocked.Increment(ref FromCount);
        if (!_redirected)
        {
            _redirected = true;
            _ = Navigation!.NavigateToAsync<FromLandingViewModel>();
        }
    }
}

/// <summary>Registers a GoBack from inside its own From (once).</summary>
public class FromGoBackViewModel : NavigationViewModelBase, INavigationAware
{
    private bool _redirected;

    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom()
    {
        if (!_redirected)
        {
            _redirected = true;
            _ = Navigation!.GoBackAsync();
        }
    }
}

/// <summary>Registers a GoBackTo from inside its own OnNavigatedFrom (once).</summary>
public class FromGoBackToViewModel : NavigationViewModelBase, INavigationAware
{
    private bool _redirected;

    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom()
    {
        if (!_redirected)
        {
            _redirected = true;
            _ = Navigation!.GoBackToAsync<TestHomeViewModel>();
        }
    }
}

/// <summary>
/// Claims any navigation whose parameter is "claimed", regardless of the
/// parameter it was last activated with.
/// </summary>
public class VoteClaimViewModel : NavigationViewModelBase, INavigationAware, INavigationTargetAware
{
    public object? LastParameter { get; private set; }

    public void OnNavigatedTo(object? parameter) => LastParameter = parameter;

    public void OnNavigatedFrom() { }

    public bool IsNavigationTarget(object? parameter) => Equals(parameter, "claimed");
}

/// <summary>
/// A recognition voter whose vote throws when the instance was activated
/// with the parameter "thrower", and claims the parameter "seek" otherwise.
/// </summary>
public class ConditionalThrowVoteViewModel : NavigationViewModelBase, INavigationAware, INavigationTargetAware
{
    private string? _stored;

    public void OnNavigatedTo(object? parameter) => _stored = parameter as string;

    public void OnNavigatedFrom() { }

    public bool IsNavigationTarget(object? parameter)
    {
        if (_stored == "thrower")
        {
            throw new InvalidOperationException("vote failed");
        }

        return Equals(parameter, "seek");
    }
}

public class ChainFirstViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ToCount;
    public static int FromCount;

    public static void Reset() { ToCount = 0; FromCount = 0; }

    public void OnNavigatedTo(object? parameter)
    {
        Interlocked.Increment(ref ToCount);
        _ = Navigation!.NavigateToAsync<ChainSecondViewModel>();
    }

    public void OnNavigatedFrom() => Interlocked.Increment(ref FromCount);
}

public class ChainSecondViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ToCount;
    public static int FromCount;

    public static void Reset() { ToCount = 0; FromCount = 0; }

    public void OnNavigatedTo(object? parameter)
    {
        Interlocked.Increment(ref ToCount);
        _ = Navigation!.NavigateToAsync<ChainThirdViewModel>();
    }

    public void OnNavigatedFrom() => Interlocked.Increment(ref FromCount);
}

public class ChainThirdViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ToCount;

    public static void Reset() => ToCount = 0;

    public void OnNavigatedTo(object? parameter) => Interlocked.Increment(ref ToCount);

    public void OnNavigatedFrom() { }
}

public class LoopAViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ConstructionCount;

    public LoopAViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;

    public void OnNavigatedTo(object? parameter) => _ = Navigation!.NavigateToAsync<LoopBViewModel>();

    public void OnNavigatedFrom() { }
}

public class LoopBViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ConstructionCount;

    public LoopBViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;

    public void OnNavigatedTo(object? parameter) => _ = Navigation!.NavigateToAsync<LoopAViewModel>();

    public void OnNavigatedFrom() { }
}

/// <summary>
/// Guard that, while <see cref="Veto"/> is set, registers a navigation of its
/// own and then vetoes the departure.
/// </summary>
public class VetoGuardViewModel : NavigationViewModelBase, INavigationAware, INavigationGuard
{
    public bool Veto { get; set; }
    public int NavigatedToCount { get; private set; }
    public int NavigatedFromCount { get; private set; }

    public void OnNavigatedTo(object? parameter) => NavigatedToCount++;

    public void OnNavigatedFrom() => NavigatedFromCount++;

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        if (Veto)
        {
            _ = Navigation!.NavigateToAsync<ButtonTargetViewModel>();
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}

/// <summary>Page whose async From blocks until the test releases it.</summary>
public class SlowFromViewModel : NavigationViewModelBase, INavigationAwareAsync
{
    public TaskCompletionSource FromStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseFrom { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
    {
        FromStarted.TrySetResult();
        await ReleaseFrom.Task;
    }
}

/// <summary>
/// Page that, from inside its own From (once), registers a refresh navigation
/// back to its own type with a new parameter.
/// </summary>
public class SelfRefreshFromViewModel : NavigationViewModelBase, INavigationAware
{
    private bool _redirected;

    public int NavigatedToCount { get; private set; }
    public int NavigatedFromCount { get; private set; }
    public object? LastParameter { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        NavigatedToCount++;
        LastParameter = parameter;
    }

    public void OnNavigatedFrom()
    {
        NavigatedFromCount++;
        if (!_redirected)
        {
            _redirected = true;
            _ = Navigation!.NavigateToAsync<SelfRefreshFromViewModel, string>("two", refreshIfActive: true);
        }
    }
}

/// <summary>Records whether the task returned by an in-callback navigation call was already completed.</summary>
public class InCallbackTaskStateViewModel : NavigationViewModelBase, INavigationAware
{
    public static bool CallbackTaskWasCompleted;

    public static void Reset() => CallbackTaskWasCompleted = false;

    public void OnNavigatedTo(object? parameter)
    {
        var task = Navigation!.NavigateToAsync<ChainLandingViewModel>();
        CallbackTaskWasCompleted = task.IsCompleted;
    }

    public void OnNavigatedFrom() { }
}

/// <summary>
/// Calls Dispose and DisposeAsync on the concrete service from inside its own
/// lifecycle callback and records what each call threw.
/// </summary>
public class DisposeInCallbackViewModel : NavigationViewModelBase, INavigationAware
{
    public Exception? DisposeError { get; private set; }
    public Exception? DisposeAsyncError { get; private set; }
    public bool DisposeAsyncThrewSynchronously { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        var svc = (NavigationService)Navigation!;
        try
        {
            svc.Dispose();
        }
        catch (Exception ex)
        {
            DisposeError = ex;
        }

        try
        {
            svc.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DisposeAsyncError = ex;
        }

        try
        {
            // Fire-and-forget: the reentrancy failure is carried by the
            // returned ValueTask, so discarding it must not throw here.
            _ = svc.DisposeAsync();
        }
        catch (InvalidOperationException)
        {
            DisposeAsyncThrewSynchronously = true;
        }
    }

    public void OnNavigatedFrom() { }
}

/// <summary>Deliberately never registered in any test container: resolving it fails.</summary>
public class UnregisteredViewModel : NavigationViewModelBase
{
}

/// <summary>Page whose async To blocks until the test releases it; records To/From ordering.</summary>
public class SlowToViewModel : NavigationViewModelBase, INavigationAwareAsync
{
    public TaskCompletionSource ToStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseTo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ToCompleted { get; private set; }
    public bool FromRanBeforeToCompleted { get; private set; }

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        ToStarted.TrySetResult();
        await ReleaseTo.Task;
        ToCompleted = true;
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
    {
        if (!ToCompleted)
        {
            FromRanBeforeToCompleted = true;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Page whose async To captures the lifecycle token it was handed and
/// blocks until released (ignoring the token), whose leave guard signals
/// its start and blocks until released, and whose From does the same —
/// so a test can force the entering step to claim a follow-up request
/// (the claim cancels the To token), withdraw that request afterwards,
/// and then prove no departure starts: a departure would invoke the
/// guard first, and the guard parks on its gate once invoked.
/// </summary>
public class SlowClaimViewModel : NavigationViewModelBase, INavigationAwareAsync, INavigationGuard
{
    public TaskCompletionSource ToStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseTo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource GuardStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseGuard { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource FromStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseFrom { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken ToToken { get; private set; }

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        ToToken = cancellationToken;
        ToStarted.TrySetResult();
        await ReleaseTo.Task;
    }

    public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        GuardStarted.TrySetResult();
        await ReleaseGuard.Task;
        return true;
    }

    public async Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
    {
        FromStarted.TrySetResult();
        await ReleaseFrom.Task;
    }
}

/// <summary>
/// Page whose async To captures the lifecycle token it was handed, signals
/// its start, and then blocks until released (ignoring the token, so a test
/// can observe the token's state while the activation is in flight).
/// </summary>
public class SlowToTokenViewModel : NavigationViewModelBase, INavigationAwareAsync
{
    public TaskCompletionSource ToStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseTo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken ToToken { get; private set; }
    public bool ToCompleted { get; private set; }

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        ToToken = cancellationToken;
        ToStarted.TrySetResult();
        await ReleaseTo.Task;
        ToCompleted = true;
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Slow-To page whose guard vetoes the first departure attempt and allows
/// later ones; records the tokens its To and From callbacks receive.
/// </summary>
public class SlowToVetoViewModel : NavigationViewModelBase, INavigationAwareAsync, INavigationGuard
{
    public TaskCompletionSource ToStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseTo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken ToToken { get; private set; }
    public CancellationToken FromToken { get; private set; }
    public bool FromRan { get; private set; }
    private int _guardCalls;

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        ToToken = cancellationToken;
        ToStarted.TrySetResult();
        await ReleaseTo.Task;
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
    {
        FromToken = cancellationToken;
        FromRan = true;
        return Task.CompletedTask;
    }

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        => Task.FromResult(Interlocked.Increment(ref _guardCalls) > 1);
}

/// <summary>
/// Slow-To page whose guard blocks until released on its first call and
/// allows later departures; records the tokens its To and From receive.
/// </summary>
public class SlowToBlockingGuardViewModel : NavigationViewModelBase, INavigationAwareAsync, INavigationGuard
{
    public TaskCompletionSource ToStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseTo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource GuardStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _guardGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken ToToken { get; private set; }
    public CancellationToken FromToken { get; private set; }
    public bool FromRan { get; private set; }
    private int _guardCalls;

    public void ReleaseGuard() => _guardGate.TrySetResult();

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        ToToken = cancellationToken;
        ToStarted.TrySetResult();
        await ReleaseTo.Task;
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default)
    {
        FromToken = cancellationToken;
        FromRan = true;
        return Task.CompletedTask;
    }

    public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _guardCalls) == 1)
        {
            GuardStarted.TrySetResult();
            await _guardGate.Task;
        }

        return true;
    }
}

/// <summary>
/// Page that registers a navigation from its own To and whose guard then
/// vetoes that very departure: the page comes to rest without ever having
/// been surfaced by its landing.
/// </summary>
public class LandingVetoViewModel : NavigationViewModelBase, INavigationAware, INavigationGuard
{
    public void OnNavigatedTo(object? parameter)
        => _ = Navigation!.NavigateToAsync<ChainLandingViewModel>();

    public void OnNavigatedFrom() { }

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);
}

/// <summary>
/// Page that lands from a chain and immediately registers a self-refresh;
/// the refresh's To then registers a follow-up navigation, so the page
/// never comes to rest between landing and leaving again.
/// </summary>
public class RefreshChainViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ToCount;

    public static void Reset() => ToCount = 0;

    public void OnNavigatedTo(object? parameter)
    {
        var call = Interlocked.Increment(ref ToCount);
        if (call == 1)
        {
            _ = Navigation!.NavigateToAsync<RefreshChainViewModel>(refreshIfActive: true);
        }
        else if (call == 2)
        {
            _ = Navigation!.NavigateToAsync<ChainLandingViewModel>();
        }
    }

    public void OnNavigatedFrom() { }
}

/// <summary>
/// Page that lands from a chain and registers a self-refresh; the refresh's
/// To registers a further request carrying a test-controlled token and then
/// blocks until released, so the test can withdraw that request (flag-only:
/// a callback-registered request has no awaiting caller to remove it from
/// the slot) while it is still sitting in the slot.
/// </summary>
public class SettleSurfaceViewModel : NavigationViewModelBase, INavigationAwareAsync
{
    public static int ToCount;
    public readonly TaskCompletionSource RefreshToStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource ReleaseRefreshTo = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly CancellationTokenSource FollowUpCts = new();

    public static void Reset() => ToCount = 0;

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref ToCount);
        if (call == 1)
        {
            _ = Navigation!.NavigateToAsync<SettleSurfaceViewModel>(refreshIfActive: true);
        }
        else if (call == 2)
        {
            _ = Navigation!.NavigateToAsync<ChainLandingViewModel>(cancellationToken: FollowUpCts.Token);
            RefreshToStarted.TrySetResult();
            await ReleaseRefreshTo.Task;
        }
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Disposable stack base page for the detach-throw test.</summary>
public class StackBaseViewModel : NavigationViewModelBase, IDisposable
{
    public static int DisposedCount;

    public static void Reset() => DisposedCount = 0;

    public void Dispose() => Interlocked.Increment(ref DisposedCount);
}

/// <summary>Disposable stack middle page for the detach-throw test: it sits
/// BELOW the throwing page, so it is queued after it in pop order.</summary>
public class StackMidViewModel : NavigationViewModelBase, IDisposable
{
    public static int DisposedCount;

    public static void Reset() => DisposedCount = 0;

    public void Dispose() => Interlocked.Increment(ref DisposedCount);
}

/// <summary>Disposable stack top page for the detach-throw test.</summary>
public class StackTopViewModel : NavigationViewModelBase, IDisposable
{
    public static int DisposedCount;

    public static void Reset() => DisposedCount = 0;

    public void Dispose() => Interlocked.Increment(ref DisposedCount);
}

/// <summary>
/// Page whose detach (SetNavigationSilent with null) throws, and which is
/// disposable so a test can observe whether its page scope was disposed.
/// </summary>
public class DetachThrowingViewModel : INavigationViewModel, IDisposable
{
    public static int DisposedCount;
    public static bool ThrowOnDetach;

    public static void Reset() { DisposedCount = 0; ThrowOnDetach = false; }

    public void Dispose() => Interlocked.Increment(ref DisposedCount);

    INavigationService? INavigationViewModel.Navigation { get; set; }

    void INavigationViewModel.SetNavigationSilent(INavigationService? service)
    {
        // The test arms the throw after setup: being covered during setup
        // also detaches the page, and only the pop's detach may throw.
        if (service is null && ThrowOnDetach)
        {
            throw new InvalidOperationException("Detach boom.");
        }
    }

    void INavigationViewModel.RaiseNavigationChanged() { }
}

/// <summary>
/// Page whose async To calls into a SECOND navigation service instance and
/// records the awaited outcome: the call must be classified as an outside
/// call for that instance (real fate), not as an in-callback registration.
/// </summary>
public class CrossCallerViewModel : NavigationViewModelBase, INavigationAwareAsync
{
    public static INavigationService? OtherService;
    public static bool? ObservedResult;
    public static TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Reset()
    {
        OtherService = null;
        ObservedResult = null;
        Done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
    {
        ObservedResult = await OtherService!.NavigateToAsync<TestHomeViewModel>();
        Done.TrySetResult();
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Page whose From registers a redirect to <see cref="ChainMidViewModel"/>.</summary>
public class ChainStartViewModel : NavigationViewModelBase, INavigationAware
{
    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom() => _ = Navigation!.NavigateToAsync<ChainMidViewModel>();
}

/// <summary>Chain middle page: its To registers a further callback request.</summary>
public class ChainMidViewModel : NavigationViewModelBase, INavigationAware
{
    public static int ConstructionCount;

    public ChainMidViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;

    public void OnNavigatedTo(object? parameter) => _ = Navigation!.NavigateToAsync<ChainEndViewModel>();

    public void OnNavigatedFrom() { }
}

/// <summary>Chain end page: must never be constructed when the chain is stopped.</summary>
public class ChainEndViewModel : NavigationViewModelBase
{
    public static int ConstructionCount;

    public ChainEndViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;
}

/// <summary>Rescue-test start page: its To registers the mid page.</summary>
public class RescueStartViewModel : NavigationViewModelBase, INavigationAware
{
    public void OnNavigatedTo(object? parameter) => _ = Navigation!.NavigateToAsync<RescueMidViewModel>();

    public void OnNavigatedFrom() { }
}

/// <summary>
/// Rescue-test middle page: its To registers the end page, and its leave
/// guard blocks until released so a test can place an outside request in
/// the slot while the (doomed) chain departure is still in its guard.
/// </summary>
public class RescueMidViewModel : NavigationViewModelBase, INavigationAware, INavigationGuard
{
    public readonly TaskCompletionSource GuardStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _guardGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseGuard() => _guardGate.TrySetResult();

    public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        GuardStarted.TrySetResult();
        await _guardGate.Task;
        return true;
    }

    public void OnNavigatedTo(object? parameter) => _ = Navigation!.NavigateToAsync<RescueEndViewModel>();

    public void OnNavigatedFrom() { }
}

/// <summary>Rescue-test end page: the chain candidate that trips the limit.</summary>
public class RescueEndViewModel : NavigationViewModelBase
{
    public static int ConstructionCount;

    public RescueEndViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;
}

/// <summary>Page whose leave guard blocks until explicitly released.</summary>
public class BlockingGuardViewModel : NavigationViewModelBase, INavigationAware, INavigationGuard
{
    public readonly TaskCompletionSource GuardStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseGuard() => _gate.TrySetResult();

    public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        GuardStarted.TrySetResult();
        await _gate.Task;
        return true;
    }

    public void OnNavigatedTo(object? parameter) { }

    public void OnNavigatedFrom() { }
}

/// <summary>
/// ViewModel whose navigation attach throws: user code failing inside a
/// hop's commit section.
/// </summary>
public class ThrowingAttachViewModel : INavigationViewModel
{
    INavigationService? INavigationViewModel.Navigation { get; set; }

    void INavigationViewModel.SetNavigationSilent(INavigationService? service)
    {
        if (service is not null)
        {
            throw new InvalidOperationException("Attach boom.");
        }
    }

    void INavigationViewModel.RaiseNavigationChanged() { }
}

/// <summary>
/// ViewModel whose navigation attach succeeds the first time and throws on
/// every later attach: stages a hop commit that throws after a
/// pop-to-existing reactivation has retired the entry's previous CTS.
/// </summary>
public class SecondAttachThrowingViewModel : INavigationViewModel
{
    private int _attachCount;

    INavigationService? INavigationViewModel.Navigation { get; set; }

    void INavigationViewModel.SetNavigationSilent(INavigationService? service)
    {
        if (service is not null && Interlocked.Increment(ref _attachCount) > 1)
        {
            throw new InvalidOperationException("Second attach boom.");
        }
    }

    void INavigationViewModel.RaiseNavigationChanged() { }
}

/// <summary>Second service type whose registration resolves to
/// <see cref="ThrowawayCountingViewModel"/> itself: navigating it creates
/// entries whose runtime type equals the other service type exactly.</summary>
public interface IThrowawayCountingTarget : INavigationViewModel
{
}

/// <summary>ViewModel that counts its own constructions (static, reset per test).</summary>
public class ThrowawayCountingViewModel : NavigationViewModelBase, IThrowawayCountingTarget
{
    public static int ConstructionCount;

    public ThrowawayCountingViewModel() => Interlocked.Increment(ref ConstructionCount);

    public static void Reset() => ConstructionCount = 0;
}

/// <summary>Derived implementation registered for the base type: resolution
/// produces this runtime type, which the exact-type pop-to-existing
/// pre-check must not anticipate.</summary>
public class DerivedThrowawayCountingViewModel : ThrowawayCountingViewModel
{
}

/// <summary>Page whose async To blocks until released and then throws.</summary>
public class SlowThrowingToViewModel : NavigationViewModelBase, INavigationAwareAsync
{
    public TaskCompletionSource ToStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseTo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        ToStarted.TrySetResult();
        await ReleaseTo.Task;
        throw new InvalidOperationException("Init failed.");
    }

    public Task OnNavigatedFromAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class KeepAliveViewModel : NavigationViewModelBase, IDisposable
{
    public bool Disposed { get; private set; }
    public int DisposeCallCount { get; private set; }

    public void Dispose()
    {
        DisposeCallCount++;
        Disposed = true;
    }
}

public abstract class BaseKeepAliveViewModel : NavigationViewModelBase
{
}

public sealed class DerivedKeepAliveViewModel : BaseKeepAliveViewModel, IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed class SecondDerivedKeepAliveViewModel : BaseKeepAliveViewModel, IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public abstract class BaseSelfTargetViewModel : NavigationViewModelBase, INavigationAware, INavigationGuard
{
    public int ToCount { get; private set; }
    public int FromCount { get; private set; }
    public int GuardCalls { get; private set; }
    public bool GuardAllows { get; set; } = true;
    public object? LastParameter { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        ToCount++;
        LastParameter = parameter;
    }

    public void OnNavigatedFrom() => FromCount++;

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        GuardCalls++;
        return Task.FromResult(GuardAllows);
    }
}

public sealed class DerivedSelfTargetViewModel : BaseSelfTargetViewModel
{
}

public sealed class ParamAwareKeepAliveViewModel : NavigationViewModelBase, INavigationAware, IDisposable
{
    public object? LastParameter { get; private set; }
    public bool Disposed { get; private set; }

    public void OnNavigatedTo(object? parameter) => LastParameter = parameter;

    public void OnNavigatedFrom()
    {
    }

    public void Dispose() => Disposed = true;
}

/// <summary>
/// A KeepAlive ViewModel whose synchronous <see cref="IDisposable.Dispose"/> always throws.
/// Used to verify that one failing disposal never blocks the disposal of the remaining ViewModels.
/// </summary>
public class ThrowingKeepAliveViewModel : NavigationViewModelBase, IDisposable
{
    public bool DisposeAttempted { get; private set; }

    public void Dispose()
    {
        DisposeAttempted = true;
        throw new InvalidOperationException("Sync dispose failed.");
    }
}

/// <summary>
/// A KeepAlive ViewModel whose <see cref="IAsyncDisposable.DisposeAsync"/> always throws.
/// </summary>
public class ThrowingAsyncDisposeViewModel : NavigationViewModelBase, IAsyncDisposable
{
    public bool DisposeAttempted { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeAttempted = true;
        throw new InvalidOperationException("Async dispose failed.");
    }
}

public class TestView { }

/// <summary>Collects weak references to every <see cref="ScopedProbeService"/> the container creates.</summary>
public sealed class ProbeRegistry
{
    public List<WeakReference> Probes { get; } = [];
}

/// <summary>
/// A scoped service that records a weak reference to itself on construction, so a
/// test can check whether it stays rooted after its page scope is disposed.
/// </summary>
public sealed class ScopedProbeService
{
    public ScopedProbeService(ProbeRegistry registry) => registry.Probes.Add(new WeakReference(this));
}

/// <summary>Page whose scope resolves a <see cref="ScopedProbeService"/>.</summary>
public sealed class ProbePageViewModel : NavigationViewModelBase
{
    public ProbePageViewModel(ScopedProbeService probe) => Probe = probe;

    public ScopedProbeService Probe { get; }
}

public class NavigationServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;

    public NavigationServiceTests()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        services.AddTransient<SimpleAwareViewModel>();
        services.AddTransient<CallerViewModel>();
        services.AddTransient<GuardedViewModel>();
        services.AddTransient<ThrowingNavigatedFromViewModel>();
        services.AddTransient<CountingAwareViewModel>();
        services.AddTransient<ThrowingNavigatedToViewModel>();
        services.AddTransient<ReentrantNavigatedToViewModel>();
        services.AddTransient<StateMismatchGuardViewModel>();
        services.AddTransient<TrackedDisposeViewModel>();
        services.AddTransient<RemoveInCallbackViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        services.AddTransient<ThrowingKeepAliveViewModel>();
        services.AddTransient<ParamAwareKeepAliveViewModel>();
        services.AddTransient<ThrowingAsyncDisposeViewModel>();
        services.AddTransient<DisposableService>();
        services.AddTransient<ServiceDependentViewModel>();
        services.AddTransient<ScopedViewModel>();
        services.AddTransient<ThrowingDependencyViewModel>();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<RefreshableViewModel>();
        services.AddTransient<ConstructionCountingViewModel>();
        services.AddTransient<BothAwareViewModel>();
        services.AddTransient<AsyncThrowingViewModel>();
        services.AddTransient<TypedAsyncViewModel>();
        services.AddTransient<ContextGuardViewModel>();
        services.AddTransient<BothGuardViewModel>();
        services.AddTransient<ContextGuardDetailViewModel>();
        services.AddTransient<CancelInNavigatedToViewModel>();
        services.AddTransient<ThinViewModel>();
        services.AddTransient<ChainLandingViewModel>();
        services.AddTransient<FromLandingViewModel>();
        services.AddTransient<ButtonTargetViewModel>();
        services.AddTransient<ToRedirectViewModel>();
        services.AddTransient<FromRedirectViewModel>();
        services.AddTransient<FromGoBackViewModel>();
        services.AddTransient<FromGoBackToViewModel>();
        services.AddTransient<VoteClaimViewModel>();
        services.AddTransient<ConditionalThrowVoteViewModel>();
        services.AddTransient<ChainFirstViewModel>();
        services.AddTransient<ChainSecondViewModel>();
        services.AddTransient<ChainThirdViewModel>();
        services.AddTransient<LoopAViewModel>();
        services.AddTransient<LoopBViewModel>();
        services.AddTransient<VetoGuardViewModel>();
        services.AddTransient<SlowFromViewModel>();
        services.AddTransient<SelfRefreshFromViewModel>();
        services.AddTransient<InCallbackTaskStateViewModel>();
        services.AddTransient<SlowToViewModel>();
        services.AddTransient<SlowClaimViewModel>();
        services.AddTransient<SlowToTokenViewModel>();
        services.AddTransient<SlowToVetoViewModel>();
        services.AddTransient<SlowToBlockingGuardViewModel>();
        services.AddTransient<SlowThrowingToViewModel>();
        services.AddTransient<LandingVetoViewModel>();
        services.AddTransient<ThrowingAttachViewModel>();
        services.AddTransient<RefreshChainViewModel>();
        services.AddTransient<BlockingGuardViewModel>();
        services.AddTransient<SettleSurfaceViewModel>();
        services.AddTransient<ChainStartViewModel>();
        services.AddTransient<ChainMidViewModel>();
        services.AddTransient<ChainEndViewModel>();
        services.AddTransient<RescueStartViewModel>();
        services.AddTransient<RescueMidViewModel>();
        services.AddTransient<RescueEndViewModel>();
        services.AddTransient<StackBaseViewModel>();
        services.AddTransient<StackMidViewModel>();
        services.AddTransient<StackTopViewModel>();
        services.AddTransient<DetachThrowingViewModel>();
        services.AddTransient<CrossCallerViewModel>();

        _serviceProvider = services.BuildServiceProvider();
        _navigationService = _serviceProvider.GetRequiredService<INavigationService>();
    }

    [Fact]
    public async Task NavigateToAsync_ShouldActivateViewModel_AndTriggerEvent()
    {
        string? notifiedRegion = null;
        INavigationViewModel? notifiedVm = null;

        _navigationService.RegionNavigated += (r, vm) =>
        {
            notifiedRegion = r;
            notifiedVm = vm;
        };

        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion");

        Assert.True(result);
        Assert.Equal("MainRegion", notifiedRegion);
        Assert.IsType<TestHomeViewModel>(notifiedVm);
        Assert.NotNull(notifiedVm.Navigation);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>("MainRegion"));
    }

    [Fact]
    public async Task NavigateToAsync_WithParameter_ShouldPassTypedParameter()
    {
        var result = await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Hello Navigation");

        Assert.True(result);
        var current = _navigationService.GetCurrentViewModel() as TestDetailViewModel;
        Assert.NotNull(current);
        Assert.Equal("Hello Navigation", current.ReceivedParam);
    }

    [Fact]
    public async Task NavigateToAsync_WithNullParameter_ShouldPassNullToAware()
    {
        var result = await _navigationService.NavigateToAsync<TestDetailViewModel, string?>(null);

        Assert.True(result);
        var current = _navigationService.GetCurrentViewModel() as TestDetailViewModel;
        Assert.NotNull(current);
        Assert.Null(current.ReceivedParam);
    }

    [Fact]
    public async Task SimpleAwareViewModel_ShouldTriggerNavigatedToAndFrom()
    {
        var navResult = await _navigationService.NavigateToAsync<SimpleAwareViewModel, int>(42);
        Assert.True(navResult);

        var simple = _navigationService.GetCurrentViewModel() as SimpleAwareViewModel;
        Assert.NotNull(simple);
        Assert.True(simple.NavigatedToCalled);
        Assert.Equal(42, simple.NavigatedToParam);

        // 切出到新页面
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.True(simple.NavigatedFromCalled);
    }

    [Fact]
    public async Task GoBackAsync_ShouldReturnToPreviousViewModel_AndDisposeCurrent()
    {
        INavigationViewModel? disposedVm = null;
        _navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Arg");

        Assert.True(_navigationService.CanGoBack());

        var goBackResult = await _navigationService.GoBackAsync();
        Assert.True(goBackResult);

        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        Assert.IsType<TestDetailViewModel>(disposedVm);
        Assert.True(((TestDetailViewModel)disposedVm).Disposed);
    }

    [Fact]
    public async Task NavigationGuard_WhenDisallowed_ShouldCancelNavigation()
    {
        await _navigationService.NavigateToAsync<GuardedViewModel>();
        var guarded = (GuardedViewModel)_navigationService.GetCurrentViewModel()!;
        guarded.AllowNavigation = false;

        var navResult = await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.False(navResult);
        Assert.True(_navigationService.IsActive<GuardedViewModel>());

        guarded.AllowNavigation = true;
        var retryResult = await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.True(retryResult);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task OnNavigatedFromThrows_ShouldCompleteTransitionAndPropagateException()
    {
        await _navigationService.NavigateToAsync<ThrowingNavigatedFromViewModel>();
        var oldViewModel = (ThrowingNavigatedFromViewModel)_navigationService.GetCurrentViewModel()!;
        oldViewModel.ThrowOnNavigatedFrom = true;

        INavigationViewModel? navigatedEventViewModel = null;
        _navigationService.RegionNavigated += (_, vm) => navigatedEventViewModel = vm;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<TestHomeViewModel>());

        Assert.Equal("OnNavigatedFrom failed.", exception.Message);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        Assert.Null(oldViewModel.Navigation);
        Assert.IsType<TestHomeViewModel>(navigatedEventViewModel);
    }

    [Fact]
    public async Task OnNavigatedToThrows_ShouldKeepCommittedPageActiveAndPropagateException()
    {
        INavigationViewModel? navigatedEventViewModel = null;
        _navigationService.RegionNavigated += (_, vm) => navigatedEventViewModel = vm;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<ThrowingNavigatedToViewModel>());

        var current = _navigationService.GetCurrentViewModel();
        Assert.Equal("OnNavigatedTo failed.", exception.Message);
        Assert.IsType<ThrowingNavigatedToViewModel>(current);
        Assert.Same(current, navigatedEventViewModel);
        Assert.Same(_navigationService, current!.Navigation);
    }

    [Fact]
    public async Task KeepAliveMode_ShouldReuseSameInstance_AndNotDisposeOnGoBack()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);

        var firstInstance = _navigationService.GetCurrentViewModel();
        Assert.IsType<KeepAliveViewModel>(firstInstance);

        // 返回 Home
        await _navigationService.GoBackAsync();
        Assert.False(((KeepAliveViewModel)firstInstance).Disposed);

        // 再次导航到 KeepAlive
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var secondInstance = _navigationService.GetCurrentViewModel();

        Assert.Same(firstInstance, secondInstance);

        // Mutual exclusion: the active KeepAlive page lives on the stack, not in
        // the cache, so ClearCacheAsync does not touch it.
        await _navigationService.ClearCacheAsync("MainRegion");
        Assert.False(((KeepAliveViewModel)firstInstance).Disposed);

        // ClearStack pops the KeepAlive page; with mutual exclusion it moves to
        // the cache (not disposed). Navigating back reuses the same instance.
        await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion", operation: NavigationOperation.Reset);
        Assert.False(((KeepAliveViewModel)firstInstance).Disposed);

        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.Same(firstInstance, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task NewMode_WhenNavigatingAwayAndBack_ShouldCreateFreshInstance_AndDisposeOldOne()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first");

        var firstInstance = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Equal("first", firstInstance.ReceivedParam);

        // Leave the page: the transient page scope is disposed.
        await _navigationService.GoBackAsync();
        Assert.True(firstInstance.Disposed);

        // Come back: a brand-new instance, no state carried over.
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("second");
        var secondInstance = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.NotSame(firstInstance, secondInstance);
        Assert.False(secondInstance.Disposed);
        Assert.Equal("second", secondInstance.ReceivedParam);
    }

    [Fact]
    public async Task ClearCache_WhenKeepAliveIsInHistory_ShouldNotAffectStackedPage()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // Mutual exclusion: the KeepAlive page is covered on the stack, not in
        // the cache. ClearCacheAsync does not touch it.
        await _navigationService.ClearCacheAsync("MainRegion");
        Assert.False(keepAlive.Disposed);

        Assert.True(await _navigationService.GoBackAsync());
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
        Assert.False(keepAlive.Disposed);

        // ClearStack pops the KeepAlive page; it moves to the cache (not disposed).
        await _navigationService.NavigateToAsync<TestHomeViewModel>("MainRegion", operation: NavigationOperation.Reset);
        Assert.False(keepAlive.Disposed);

        // Navigating back to it reuses the cached instance.
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task KeepAlive_WhenCachedInstanceDeeperOnStack_PopsBackToIt()
    {
        // Mainstream behavior: navigating to a cached KeepAlive page whose instance is
        // already on the stack pops back to it instead of pushing a duplicate.
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestDetailViewModel>();
        var detail = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;

        int navigatedEvents = 0;
        _navigationService.RegionNavigated += (_, _) => navigatedEvents++;

        var result = await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
        // The Detail page was popped; the Home page below remains.
        Assert.True(_navigationService.CanGoBack());
        Assert.Equal(1, navigatedEvents);

        // The popped Detail page's scope was disposed; the shared KeepAlive scope survives.
        Assert.True(detail.Disposed);
        Assert.False(keepAlive.Disposed);

        // The page is still cached and reusable afterwards.
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive));
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
        Assert.False(keepAlive.Disposed);
    }

    [Fact]
    public async Task NavigateToAsync_PopToExisting_WithSameParameter_ReusesInstance()
    {
        await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", retention: NavigationRetention.KeepAlive);
        var vm = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // The covered instance is recognized by its parameter and popped back
        // to — the same instance is reactivated rather than a new one created.
        var result = await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal("first", vm.LastParameter);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_PopToExisting_WithDifferentParameter_DoesNotReuseInstance()
    {
        await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", retention: NavigationRetention.KeepAlive);
        var first = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // A different parameter addresses a different page: the existing
        // instance is NOT re-purposed with the new parameter (the former
        // type-match behavior); a new instance is pushed above it.
        var result = await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "second", retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        var second = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.Equal("second", second.LastParameter);
        Assert.Equal("first", first.LastParameter);

        // Navigating back to the first parameter recognizes the original
        // instance below and pops to it.
        var back = await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", retention: NavigationRetention.KeepAlive);

        Assert.True(back);
        Assert.Same(first, _navigationService.GetCurrentViewModel());
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_WhenSingletonOnAnotherRegionStack_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<SingletonViewModel>("Region1");

        // Same-region would pop back to the instance; another region cannot share it:
        // a ViewModel has a single Navigation reference.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => navigationService.NavigateToAsync<SingletonViewModel>("Region2"));

        Assert.Contains("another region's", ex.Message);
        Assert.Null(navigationService.GetCurrentViewModel("Region2"));
        Assert.True(navigationService.IsActive<SingletonViewModel>("Region1"));

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ClearCache_WhenSingletonCachedInTwoRegions_ShouldNeverDisposeIt()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<KeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<TestHomeViewModel>("Region1");
        await navigationService.NavigateToAsync<KeepAliveViewModel>("Region1", retention: NavigationRetention.KeepAlive);
        var keepAlive = (KeepAliveViewModel)navigationService.GetCurrentViewModel("Region1")!;
        await navigationService.GoBackAsync("Region1");

        await navigationService.NavigateToAsync<TestHomeViewModel>("Region2");
        await navigationService.NavigateToAsync<KeepAliveViewModel>("Region2", retention: NavigationRetention.KeepAlive);
        Assert.Same(keepAlive, navigationService.GetCurrentViewModel("Region2"));

        await navigationService.ClearCacheAsync("Region1");

        // A singleton belongs to the root container. Clearing a region cache must never
        // dispose it, even when its page scope is torn down.
        Assert.False(keepAlive.Disposed);

        await navigationService.GoBackAsync("Region2");
        await navigationService.NavigateToAsync<KeepAliveViewModel>("Region2", retention: NavigationRetention.KeepAlive);
        Assert.Same(keepAlive, navigationService.GetCurrentViewModel("Region2"));
        Assert.False(keepAlive.Disposed);

        await navigationService.DisposeAsync();
        Assert.False(keepAlive.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task NavigateToAsync_WhenGuardRegistersReplace_ShouldSupersedePendingRequest()
    {
        TrackedDisposeViewModel.DisposeCount = 0;

        await _navigationService.NavigateToAsync<StateMismatchGuardViewModel>();
        var result = await _navigationService.NavigateToAsync<TrackedDisposeViewModel>();

        // The pending request was superseded: the Replace the guard registered
        // was the last request in the window and won.
        Assert.False(result);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        // The superseded request never constructed its page, so there is no
        // resolved-but-unused instance to dispose.
        Assert.Equal(0, TrackedDisposeViewModel.DisposeCount);
    }

    [Fact]
    public async Task ClearCacheAsync_ThenGoBack_KeepAliveMovesToCache()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;

        // Mutual exclusion: the active KeepAlive page is on the stack, not in
        // the cache. ClearCacheAsync does not touch it.
        await _navigationService.ClearCacheAsync("MainRegion");
        Assert.False(keepAlive.Disposed);
        Assert.True(_navigationService.IsActive<KeepAliveViewModel>());

        Assert.True(await _navigationService.GoBackAsync());

        // Popping moves the KeepAlive page to the cache (not disposed).
        Assert.False(keepAlive.Disposed);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());

        // Navigating back reuses the cached instance.
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public void DisposedPageScope_DoesNotRootItsScopedServices()
    {
        // Regression (round-5 review P0-1): the exactly-once disposal arbitration
        // used to keep a strong reference to every disposed scope forever, and a
        // disposed MS DI scope keeps its resolved scoped services in its
        // resolved-services table — so every visited page's whole scoped graph
        // stayed rooted for the (singleton) navigation service's lifetime. The
        // arbitration now holds scopes weakly: once the page is left (Destroy)
        // and forgotten, its scoped services must become collectable while the
        // navigation service itself is still alive and in use.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<ProbeRegistry>();
        services.AddScoped<ScopedProbeService>();
        services.AddTransient<ProbePageViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();
        var registry = provider.GetRequiredService<ProbeRegistry>();

        // The navigation runs on a dedicated thread: completed navigation
        // tasks and their state machines (which transitively reference the
        // departed page's entry and scope) must not be reachable from this
        // test's own async frame when the collection is checked. Failures on
        // that thread are ferried back and rethrown here — an unhandled
        // exception on a raw Thread would kill the whole test host.
        List<WeakReference>? probes = null;
        Exception? threadError = null;
        var thread = new Thread(() =>
        {
            try
            {
                probes = CaptureProbesThenLeave(nav, registry);
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
        });
        thread.Start();
        thread.Join();

        if (threadError is not null)
        {
            ExceptionDispatchInfo.Capture(threadError).Throw();
        }

        Assert.NotNull(probes);
        Assert.NotEmpty(probes);
        AssertAllCollectable(probes);

        // The navigation service outlives the collected page (and this use
        // after the GC point also keeps it rooted across the assertions).
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        GC.KeepAlive(provider);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> CaptureProbesThenLeave(INavigationService nav, ProbeRegistry registry)
    {
        nav.NavigateToAsync<ProbePageViewModel>().GetAwaiter().GetResult();
        var probes = registry.Probes.ToList();
        Assert.All(probes, probe => Assert.True(probe.IsAlive)); // rooted by the active page's entry

        // Replace the page (Destroy retention): the entry leaves the stack
        // and its scope is disposed during the departure. (A Push would only
        // cover the page — its entry and scope stay on the stack.)
        nav.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace).GetAwaiter().GetResult();
        return probes;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertAllCollectable(List<WeakReference> probes)
    {
        for (var attempt = 0; attempt < 5 && probes.Any(probe => probe.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.All(probes, probe => Assert.False(probe.IsAlive));
    }

    [Fact]
    public async Task KeepAlive_NavigatedViaBaseType_ShouldReuseInstanceAfterPop()
    {
        // Regression: the cache key must be the static type used at navigation
        // time, not the runtime type. DI resolves DerivedKeepAliveViewModel for
        // BaseKeepAliveViewModel; pop writes the cache under the static key and
        // the next base-type navigation must find it.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<BaseKeepAliveViewModel, DerivedKeepAliveViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<BaseKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var first = nav.GetCurrentViewModel();
        Assert.IsType<DerivedKeepAliveViewModel>(first);

        await nav.GoBackAsync();
        await nav.NavigateToAsync<BaseKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);

        Assert.Same(first, nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task SelfNavigation_ViaBaseType_IsNoOpWithoutGuard()
    {
        // The self-target check must compare against the static type the
        // page was navigated to, not the runtime type: DI resolves the
        // derived implementation, yet navigating to the base type again
        // targets the already-active page — a no-op that never consults
        // the guard and never runs From.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<BaseSelfTargetViewModel, DerivedSelfTargetViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<BaseSelfTargetViewModel>();
        var vm = Assert.IsType<DerivedSelfTargetViewModel>(nav.GetCurrentViewModel());
        Assert.Equal(1, vm.ToCount);

        // Even a vetoing guard is never asked: the request is a no-op.
        vm.GuardAllows = false;
        var result = await nav.NavigateToAsync<BaseSelfTargetViewModel>();

        Assert.True(result);
        Assert.Equal(0, vm.GuardCalls);
        Assert.Equal(0, vm.FromCount);
        Assert.Equal(1, vm.ToCount);
        Assert.Same(vm, nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task SelfRefresh_ViaBaseType_RefreshesInPlace()
    {
        // Same static-type rule for refresh: the active page is refreshed
        // in place (To again with the new parameter, no From, same
        // instance) instead of running a full leave-and-enter cycle.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<BaseSelfTargetViewModel, DerivedSelfTargetViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<BaseSelfTargetViewModel, string>("first");
        var vm = Assert.IsType<DerivedSelfTargetViewModel>(nav.GetCurrentViewModel());
        Assert.Equal("first", vm.LastParameter);

        var result = await nav.NavigateToAsync<BaseSelfTargetViewModel, string>("second", refreshIfActive: true);

        Assert.True(result);
        Assert.Same(vm, nav.GetCurrentViewModel());
        Assert.Equal(0, vm.FromCount);
        Assert.Equal(2, vm.ToCount);
        Assert.Equal("second", vm.LastParameter);
    }

    [Fact]
    public async Task KeepAliveCacheEviction_WhenSecondSameKeyInstancePops_ShouldDisposeEvictedEntry()
    {
        // Regression: the cache holds one entry per (region, static type) key.
        // A DI factory returning different runtime types for the same static
        // type puts two same-key KeepAlive instances on the stack; when both
        // pop, the second cache write evicts the first entry. The evicted
        // entry's scope must be disposed, not orphaned.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        int counter = 0;
        services.AddTransient<BaseKeepAliveViewModel>(_ =>
            (counter++ % 2 == 0) ? new DerivedKeepAliveViewModel() : new SecondDerivedKeepAliveViewModel());
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<BaseKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var first = Assert.IsType<DerivedKeepAliveViewModel>(nav.GetCurrentViewModel());
        await nav.NavigateToAsync<BaseKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var second = Assert.IsType<SecondDerivedKeepAliveViewModel>(nav.GetCurrentViewModel());

        // Pop the second instance: it moves to the cache.
        Assert.True(await nav.GoBackAsync());
        Assert.False(second.Disposed);

        // Pop the first instance: its cache write evicts the second entry.
        Assert.True(await nav.GoBackAsync());
        Assert.True(second.Disposed);
        Assert.False(first.Disposed);

        // The surviving cache entry is the first instance and is reused.
        await nav.NavigateToAsync<BaseKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.Same(first, nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task ClearCacheAsync_WhenCacheIsEmpty_ShouldNotRaiseRegionCacheCleared()
    {
        string? clearedRegion = null;
        _navigationService.RegionCacheCleared += r => clearedRegion = r;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.ClearCacheAsync("MainRegion");
        Assert.Null(clearedRegion);

        // With an actual cached entry, the event does fire.
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        await _navigationService.GoBackAsync();
        await _navigationService.ClearCacheAsync("MainRegion");
        Assert.Equal("MainRegion", clearedRegion);
    }

    [Fact]
    public async Task ClearAllCacheAsync_WhenCachesAreEmpty_ShouldNotRaiseRegionCacheCleared()
    {
        var clearedRegions = new List<string>();
        _navigationService.RegionCacheCleared += r => clearedRegions.Add(r);

        // Regions exist (stacks) but hold no cached entries.
        await _navigationService.NavigateToAsync<TestHomeViewModel>("Region1");
        await _navigationService.NavigateToAsync<TestHomeViewModel>("Region2");
        await _navigationService.ClearAllCacheAsync();

        Assert.Empty(clearedRegions);
    }

    [Fact]
    public async Task ReplaceMode_ShouldNotEvictDistinctKeepAlivePageDeeperInStack()
    {
        // Regression: Replace's duplicate removal must match by instance only.
        // A different-instance KeepAlive page of the same type deeper in the
        // stack is a distinct page and must survive a Replace on top.
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        await _navigationService.NavigateToAsync<TestDetailViewModel>();

        // Replace the top page with a fresh KeepAlive instance.
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("MainRegion", operation: NavigationOperation.Replace);
        Assert.True(_navigationService.IsActive<KeepAliveViewModel>());
        Assert.NotSame(keepAlive, _navigationService.GetCurrentViewModel());

        // The deeper KeepAlive page is still on the stack.
        Assert.True(await _navigationService.GoBackAsync());
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
        Assert.False(keepAlive.Disposed);
    }

    [Fact]
    public async Task ReplaceMode_ShouldReplaceCurrentPage()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Param1");
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(operation: NavigationOperation.Replace);

        Assert.True(_navigationService.IsActive<KeepAliveViewModel>());
        Assert.True(_navigationService.CanGoBack());

        // 返回应直接回到 HomeViewModel，跳过被 Replace 的 DetailViewModel
        await _navigationService.GoBackAsync();
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task ClearStackMode_ShouldEmptyStackAndPushNew()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("Param");

        Assert.True(_navigationService.CanGoBack());

        await _navigationService.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Reset);

        Assert.False(_navigationService.CanGoBack());
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task GetCurrentRetention_And_GetCurrentViewModel_ShouldReflectState()
    {
        Assert.Null(_navigationService.GetCurrentRetention());
        Assert.Null(_navigationService.GetCurrentViewModel());
        Assert.False(_navigationService.CanGoBack());
        Assert.False(_navigationService.IsActive<TestHomeViewModel>());

        await _navigationService.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Push, retention: NavigationRetention.Destroy);
        Assert.Equal(NavigationRetention.Destroy, _navigationService.GetCurrentRetention());
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        Assert.False(_navigationService.CanGoBack());

        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.Equal(NavigationRetention.KeepAlive, _navigationService.GetCurrentRetention());
        Assert.IsType<KeepAliveViewModel>(_navigationService.GetCurrentViewModel());
        Assert.True(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task ModeSplit_ReplaceWithKeepAlive_OutgoingUsesOwnRetention_NewPageCachesOnLeave()
    {
        // The outgoing page's fate is governed by its OWN retention; the
        // incoming page's retention is recorded on its entry and governs
        // its own later departure. Replace + KeepAlive composes both.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        var home = _navigationService.GetCurrentViewModel();

        // Replace Home (Destroy) with a KeepAlive page: Home is destroyed
        // (its own retention), the stack does not grow.
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(
            operation: NavigationOperation.Replace, retention: NavigationRetention.KeepAlive));
        var kept = _navigationService.GetCurrentViewModel();
        Assert.False(_navigationService.CanGoBack());

        // Home really was destroyed — proven through the cache: had the
        // Replace wrongly cached it (per the incoming request's retention),
        // this KeepAlive navigation would reuse the same instance.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.NotSame(home, _navigationService.GetCurrentViewModel());
        Assert.True(await _navigationService.GoBackAsync());

        // Replace the KeepAlive page away: it leaves per its own retention
        // — into the cache, not destroyed — so returning reuses it.
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>(
            operation: NavigationOperation.Replace));
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.Same(kept, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task ModeSplit_ResetWithKeepAlive_OutgoingKeepAliveCaches_AndCacheSurvivesReset()
    {
        // Seed the cache with a KeepAlive page, then build a mixed stack
        // and Reset it: outgoing pages follow their own retention, and
        // Reset does not clear the cache.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(
            retention: NavigationRetention.KeepAlive));
        var seeded = _navigationService.GetCurrentViewModel();
        Assert.True(await _navigationService.GoBackAsync()); // seeded -> cache

        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>(
            retention: NavigationRetention.KeepAlive));
        var detail = _navigationService.GetCurrentViewModel();

        Assert.True(await _navigationService.NavigateToAsync<ButtonTargetViewModel>(
            operation: NavigationOperation.Reset, retention: NavigationRetention.KeepAlive));
        Assert.False(_navigationService.CanGoBack());
        Assert.IsType<ButtonTargetViewModel>(_navigationService.GetCurrentViewModel());

        // The Reset outgoing Detail (KeepAlive) went to the cache...
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.Same(detail, _navigationService.GetCurrentViewModel());

        // ...and the cache entry seeded before the Reset survived it.
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.Same(seeded, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task GoBackAsync_WhenStackHasSingleOrNoItem_ShouldReturnFalse()
    {
        // 空栈
        var emptyBack = await _navigationService.GoBackAsync("EmptyRegion");
        Assert.False(emptyBack);

        // 仅有一个页面
        await _navigationService.NavigateToAsync<TestHomeViewModel>("SingleRegion");
        var singleBack = await _navigationService.GoBackAsync("SingleRegion");
        Assert.False(singleBack);
    }

    [Fact]
    public async Task ClearCache_Sync_ShouldDisposeAndTriggerEvents()
    {
        string? clearedRegion = null;
        INavigationViewModel? disposedVm = null;

        _navigationService.RegionCacheCleared += r => clearedRegion = r;
        _navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        // Mutual exclusion: GoBack pops the page so it moves to the cache,
        // then the synchronous ClearCache will dispose it.
        await _navigationService.GoBackAsync();
        _navigationService.ClearCache("MainRegion");

        Assert.Equal("MainRegion", clearedRegion);
        Assert.NotNull(disposedVm);
        Assert.IsType<KeepAliveViewModel>(disposedVm);
        Assert.True(((KeepAliveViewModel)disposedVm).Disposed);
    }

    [Fact]
    public async Task ClearAllCacheAsync_ShouldDisposeAllCachedInstances()
    {
        var clearedRegions = new HashSet<string>();
        int disposedCount = 0;

        _navigationService.RegionCacheCleared += r => clearedRegions.Add(r);
        _navigationService.ViewModelReleased += (_, _) => disposedCount++;

        await _navigationService.NavigateToAsync<TestHomeViewModel>("Region1");
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("Region1", retention: NavigationRetention.KeepAlive);
        await _navigationService.NavigateToAsync<TestHomeViewModel>("Region2");
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("Region2", retention: NavigationRetention.KeepAlive);
        // Mutual exclusion: GoBack pops the pages so they move to the cache.
        await _navigationService.GoBackAsync("Region1");
        await _navigationService.GoBackAsync("Region2");

        await _navigationService.ClearAllCacheAsync();

        Assert.Contains("Region1", clearedRegions);
        Assert.Contains("Region2", clearedRegions);
        Assert.Equal(2, disposedCount);
    }

    [Fact]
    public async Task Dispose_Sync_ShouldDisposeStacksAndCaches()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        var sp = services.BuildServiceProvider();
        var navService = (NavigationService)sp.GetRequiredService<INavigationService>();

        await navService.NavigateToAsync<TestHomeViewModel>();
        await navService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var keepAlive = (KeepAliveViewModel)navService.GetCurrentViewModel()!;
        await navService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);

        bool disposedEventTriggered = false;
        navService.ViewModelReleased += (_, _) => disposedEventTriggered = true;

        navService.Dispose();
        Assert.True(disposedEventTriggered);
        Assert.Equal(1, keepAlive.DisposeCallCount);

        // 验证已释放后再次调用是幂等的
        navService.Dispose();
        await navService.DisposeAsync();

        // 验证已释放后调用导航抛出 ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => navService.NavigateToAsync<TestHomeViewModel>());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => navService.GoBackAsync());
    }

    [Fact]
    public async Task DisposeAndDisposeAsync_InLifecycleCallback_ThrowInvalidOperationException()
    {
        // D-1: like the ClearCache family, Dispose/DisposeAsync called from
        // within a page lifecycle callback fail fast instead of disposing the
        // service (and the caller's own page scope) mid-callback — DisposeAsync
        // previously also waited out the full disposal gate timeout first.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<DisposeInCallbackViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<DisposeInCallbackViewModel>();
        var vm = Assert.IsType<DisposeInCallbackViewModel>(nav.GetCurrentViewModel());

        var disposeError = Assert.IsType<InvalidOperationException>(vm.DisposeError);
        Assert.Contains("Dispose cannot be called from within a page lifecycle callback", disposeError.Message);
        var disposeAsyncError = Assert.IsType<InvalidOperationException>(vm.DisposeAsyncError);
        Assert.Contains("DisposeAsync cannot be called from within a page lifecycle callback", disposeAsyncError.Message);
        Assert.False(vm.DisposeAsyncThrewSynchronously);

        // Neither call disposed the service: it is still usable.
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
    }

    [Fact]
    public async Task AfterDisposeAsync_QueriesReportEmptyState_AndCacheClearsAreSilentNoOps()
    {
        // D-2 contract (async disposal leg): after disposal, navigation
        // throws ObjectDisposedException, but queries report the actual
        // (empty) state without throwing, cache clears are silent no-ops,
        // and subscribing stays valid.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<TestDetailViewModel>();
        Assert.True(nav.CanGoBack());

        // DisposeAsync: TestDetailViewModel is async-disposable only, which
        // the synchronous Dispose path rejects by design.
        await nav.DisposeAsync();

        await AssertPostDisposeMatrixAsync(nav);
    }

    [Fact]
    public async Task AfterDispose_QueriesReportEmptyState_AndCacheClearsAreSilentNoOps()
    {
        // D-2 contract (sync disposal leg): same matrix as the async leg.
        // (Two distinct page types: re-navigating to the active type would
        // be a self no-op and the stack would never reach depth two.)
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<KeepAliveViewModel>();
        Assert.True(nav.CanGoBack());

        nav.Dispose();

        await AssertPostDisposeMatrixAsync(nav);
    }

    private static async Task AssertPostDisposeMatrixAsync(NavigationService nav)
    {
        await Assert.ThrowsAsync<ObjectDisposedException>(() => nav.NavigateToAsync<TestHomeViewModel>());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => nav.GoBackAsync());

        Assert.False(nav.CanGoBack());
        Assert.Null(nav.GetCurrentViewModel());
        Assert.False(nav.IsActive<TestHomeViewModel>());
        Assert.Null(nav.GetCurrentRetention());
        Assert.Empty(nav.GetNavigationStack());

        nav.ClearCache("MainRegion");
        nav.ClearAllCache();
        await nav.ClearCacheAsync("MainRegion");
        await nav.ClearAllCacheAsync();

        // Subscribing (and unsubscribing) stays valid after disposal.
        using var subscription = nav.SubscribeRegionNavigated((_, _) => { });
    }

    [Fact]
    public async Task BaseViewModel_ProtectedMethods_ShouldWorkWhenNavAttached_AndGracefullyFallback()
    {
        // 1. 当未附加 Navigation 时，调用方法优雅回退 false
        var detachedCaller = new CallerViewModel();
        Assert.False(await detachedCaller.TriggerNavigateToHomeAsync());
        Assert.False(await detachedCaller.TriggerNavigateToDetailAsync("test"));
        Assert.False(await detachedCaller.TriggerGoBackAsync());

        // 2. 当附加 Navigation 时，通过 CallerViewModel 内部发起导航
        await _navigationService.NavigateToAsync<CallerViewModel>();
        var attachedCaller = (CallerViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotNull(attachedCaller.Navigation);

        var navResult = await attachedCaller.TriggerNavigateToDetailAsync("from-caller");
        Assert.True(navResult);
        Assert.True(_navigationService.IsActive<TestDetailViewModel>());

        // 3. 切出到新页面后，老页面的 Navigation 引用被置空（无法在切出状态下越权导航）
        Assert.Null(attachedCaller.Navigation);
        Assert.False(await attachedCaller.TriggerGoBackAsync());

        // 4. 通过导航服务退栈回到 CallerViewModel，Navigation 重新注入
        await _navigationService.GoBackAsync();
        Assert.True(_navigationService.IsActive<CallerViewModel>());
        Assert.NotNull(attachedCaller.Navigation);

        // 5. 重新激活后可继续正常导航
        var homeNav = await attachedCaller.TriggerNavigateToHomeAsync();
        Assert.True(homeNav);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task AddElegantSeriesFlow_Scoped_ShouldIsolateNavigationStacksBetweenScopes()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow(ServiceLifetime.Scoped);
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var sp = services.BuildServiceProvider();

        await using var scope1 = sp.CreateAsyncScope();
        await using var scope2 = sp.CreateAsyncScope();

        var nav1 = scope1.ServiceProvider.GetRequiredService<INavigationService>();
        var nav2 = scope2.ServiceProvider.GetRequiredService<INavigationService>();

        Assert.NotSame(nav1, nav2);

        await nav1.NavigateToAsync<TestHomeViewModel>();
        await nav2.NavigateToAsync<TestDetailViewModel, string>("scope2");

        Assert.True(nav1.IsActive<TestHomeViewModel>());
        Assert.False(nav1.IsActive<TestDetailViewModel>());

        Assert.True(nav2.IsActive<TestDetailViewModel>());
        Assert.False(nav2.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public void ViewForAttribute_ShouldStoreViewModelType_AndThrowOnNull()
    {
        var attr = new ViewForAttribute(typeof(TestHomeViewModel));
        Assert.Equal(typeof(TestHomeViewModel), attr.ViewModelType);

        Assert.Throws<ArgumentNullException>(() => new ViewForAttribute(null!));
    }

    [Fact]
    public void ServiceCollectionExtensions_ShouldThrowOnNullServices()
    {
        Assert.Throws<ArgumentNullException>(() => FlowServiceCollectionExtensions.AddElegantSeriesFlow(null!));
        Assert.Throws<ArgumentNullException>(() => FlowServiceCollectionExtensions.AddElegantSeriesFlow(null!, ServiceLifetime.Scoped));
    }

    [Fact]
    public void AddElegantSeriesFlow_ShouldThrowOnTransientLifetime()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddElegantSeriesFlow(ServiceLifetime.Transient));
    }

    [Fact]
    public void AddElegantSeriesFlow_ShouldThrowOnUndefinedLifetime()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddElegantSeriesFlow((ServiceLifetime)999));
    }

    [Fact]
    public async Task Validation_AllMethods_ShouldThrowOnInvalidRegionName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.NavigateToAsync<TestHomeViewModel>(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.NavigateToAsync<TestDetailViewModel, string>("param", "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.GoBackAsync("  "));
        Assert.Throws<ArgumentException>(() => _navigationService.CanGoBack(""));
        Assert.Throws<ArgumentException>(() => _navigationService.GetCurrentViewModel("   "));
        Assert.Throws<ArgumentException>(() => _navigationService.IsActive<TestHomeViewModel>(""));
        Assert.Throws<ArgumentException>(() => _navigationService.GetCurrentRetention("  "));
        Assert.Throws<ArgumentException>(() => _navigationService.ClearCache(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _navigationService.ClearCacheAsync("  ").AsTask());
    }

    [Fact]
    public async Task NavigateToAsync_ReentrancyInRegionNavigated_ShouldNotDeadlock()
    {
        bool secondNavigated = false;

        void Handler(string region, INavigationViewModel vm)
        {
            if (vm is TestHomeViewModel)
            {
                // In RegionNavigated callback, trigger another navigation (re-entrancy test)
                _ = Task.Run(async () =>
                {
                    await _navigationService.NavigateToAsync<TestDetailViewModel, string>("nested");
                    secondNavigated = true;
                });
            }
        }

        _navigationService.RegionNavigated += Handler;
        try
        {
            var firstNav = await _navigationService.NavigateToAsync<TestHomeViewModel>();
            Assert.True(firstNav);

            // Wait briefly for nested task completion
            for (int i = 0; i < 50 && !secondNavigated; i++)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(secondNavigated);
            Assert.True(_navigationService.IsActive<TestDetailViewModel>());
        }
        finally
        {
            _navigationService.RegionNavigated -= Handler;
        }
    }

    [Fact]
    public async Task NavigateToAsync_WhenOnNavigatedToNavigatesAgain_ShouldNotDeadlock()
    {
        // Run the outer navigation on a worker thread: a deadlock inside OnNavigatedTo
        // surfaces as a timeout here instead of hanging the whole test suite.
        var outerTask = Task.Run(() => _navigationService.NavigateToAsync<ReentrantNavigatedToViewModel>());

        var winner = await Task.WhenAny(outerTask, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Same(outerTask, winner);

        Assert.True(await outerTask);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>());
    }

    [Fact]
    public async Task ClearCacheAsync_ShouldRaiseRegionCacheCleared_AndViewModelReleasedEvents()
    {
        string? clearedRegion = null;
        INavigationViewModel? disposedVm = null;

        _navigationService.RegionCacheCleared += r => clearedRegion = r;
        _navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        // Mutual exclusion: GoBack pops the page so it moves to the cache,
        // then ClearCacheAsync will dispose it.
        await _navigationService.GoBackAsync();
        await _navigationService.ClearCacheAsync("MainRegion");

        Assert.Equal("MainRegion", clearedRegion);
        Assert.NotNull(disposedVm);
        Assert.IsType<KeepAliveViewModel>(disposedVm);
        Assert.True(((KeepAliveViewModel)disposedVm).Disposed);
    }

    [Fact]
    public void NavigationAware_TypeMismatch_ShouldThrowArgumentExceptionWithEnglishMessage()
    {
        INavigationAware aware = new TestDetailViewModel();
        var ex = Assert.Throws<ArgumentException>(() => aware.OnNavigatedTo(12345));

        Assert.Contains("Navigation parameter type mismatch: expected String, but got Int32.", ex.Message);
    }

    [Fact]
    public async Task DisposeAsync_ShouldRaiseEventsOutOfLock_AndNotDeadlock()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<KeepAliveViewModel>();
        var sp = services.BuildServiceProvider();
        var navService = (NavigationService)sp.GetRequiredService<INavigationService>();

        await navService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var keepAlive = (KeepAliveViewModel)navService.GetCurrentViewModel()!;
        await navService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);

        bool callbackExecuted = false;
        navService.RegionCacheCleared += region =>
        {
            // If events were raised while holding _navigationLock, calling CanGoBack or other methods could lock
            callbackExecuted = true;
            bool canGoBack = navService.CanGoBack(region);
            Assert.False(canGoBack);
        };

        await navService.DisposeAsync();
        Assert.True(callbackExecuted);
        Assert.Equal(1, keepAlive.DisposeCallCount);
    }

    [Fact]
    public async Task ClearAllCache_WhenStackIsEmpty_ShouldStillNotifyCachedRegion()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>("EmptyStackRegion");
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("EmptyStackRegion", retention: NavigationRetention.KeepAlive);

        // Pop back to home, then clear stack
        await _navigationService.GoBackAsync("EmptyStackRegion");
        await _navigationService.NavigateToAsync<TestHomeViewModel>("EmptyStackRegion", operation: NavigationOperation.Reset);

        var notifiedRegions = new List<string>();
        _navigationService.RegionCacheCleared += r => notifiedRegions.Add(r);

        _navigationService.ClearAllCache();

        Assert.Contains("EmptyStackRegion", notifiedRegions);
    }

    // ────────────────── Disposal-failure isolation (P1 regression) ──────────────────

    /// <summary>
    /// Puts a KeepAlive ViewModel into the cache and then off the navigation stack,
    /// so a later cache clear disposes it directly.
    /// </summary>
    private async Task<ThrowingKeepAliveViewModel> CacheThrowingViewModelAsync(string regionName)
    {
        await _navigationService.NavigateToAsync<ThrowingKeepAliveViewModel>(regionName, retention: NavigationRetention.KeepAlive);
        var vm = (ThrowingKeepAliveViewModel)_navigationService.GetCurrentViewModel(regionName)!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>(regionName, operation: NavigationOperation.Replace);
        return vm;
    }

    private async Task<KeepAliveViewModel> CacheHealthyViewModelAsync(string regionName)
    {
        await _navigationService.NavigateToAsync<KeepAliveViewModel>(regionName, retention: NavigationRetention.KeepAlive);
        var vm = (KeepAliveViewModel)_navigationService.GetCurrentViewModel(regionName)!;
        await _navigationService.NavigateToAsync<TestHomeViewModel>(regionName, operation: NavigationOperation.Replace);
        return vm;
    }

    [Fact]
    public async Task NavigateAway_WhenPageScopeDisposalThrows_ViewModelReleasedStillFires_PinOwnershipReleased()
    {
        INavigationViewModel? releasedVm = null;
        string? releasedRegion = null;
        _navigationService.ViewModelReleased += (region, vm) => { releasedRegion = region; releasedVm = vm; };

        await _navigationService.NavigateToAsync<ThrowingKeepAliveViewModel>("EventRegion");
        var throwing = (ThrowingKeepAliveViewModel)_navigationService.GetCurrentViewModel("EventRegion")!;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<TestHomeViewModel>("EventRegion", operation: NavigationOperation.Replace));
        Assert.Equal("Sync dispose failed.", ex.Message);

        // Ownership was released even though the scope disposal threw; the error still
        // propagates to the caller, and navigation itself completed.
        Assert.Same(throwing, releasedVm);
        Assert.Equal("EventRegion", releasedRegion);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(_navigationService.IsActive<TestHomeViewModel>("EventRegion"));
    }

    [Fact]
    public async Task ClearCacheAsync_WhenOneViewModelThrows_ShouldStillDisposeRemaining()
    {
        var throwing = await CacheThrowingViewModelAsync("CacheRegion");
        var healthy = await CacheHealthyViewModelAsync("CacheRegion");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.ClearCacheAsync("CacheRegion").AsTask());

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(healthy.Disposed);
    }

    [Fact]
    public async Task ClearCache_WhenOneViewModelThrows_ShouldStillDisposeRemaining()
    {
        var throwing = await CacheThrowingViewModelAsync("SyncCacheRegion");
        var healthy = await CacheHealthyViewModelAsync("SyncCacheRegion");

        // Exercise the synchronous clear path; only the setup above is asynchronous.
        var ex = Assert.Throws<InvalidOperationException>(() => _navigationService.ClearCache("SyncCacheRegion"));

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(healthy.Disposed);
    }

    [Fact]
    public async Task ClearAllCacheAsync_WhenMultipleViewModelsThrow_ShouldAggregateExceptions()
    {
        var throwingA = await CacheThrowingViewModelAsync("RegionA");
        var throwingB = await CacheThrowingViewModelAsync("RegionB");

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => _navigationService.ClearAllCacheAsync().AsTask());

        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.True(throwingA.DisposeAttempted);
        Assert.True(throwingB.DisposeAttempted);
    }

    [Fact]
    public async Task DisposeAsync_WhenCacheViewModelThrows_ShouldStillDisposeStackViewModels()
    {
        var throwing = await CacheThrowingViewModelAsync("DisposeRegion");

        // A healthy KeepAlive ViewModel that stays on the navigation stack.
        await _navigationService.NavigateToAsync<KeepAliveViewModel>("DisposeRegion", retention: NavigationRetention.KeepAlive);
        var stacked = (KeepAliveViewModel)_navigationService.GetCurrentViewModel("DisposeRegion")!;

        var navService = (NavigationService)_navigationService;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => navService.DisposeAsync().AsTask());

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(stacked.Disposed);
    }

    [Fact]
    public async Task Dispose_WhenStackViewModelThrows_ShouldStillDisposeRemainingStackViewModels()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ThrowingKeepAliveViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var sp = services.BuildServiceProvider();
        var navService = (NavigationService)sp.GetRequiredService<INavigationService>();

        await navService.NavigateToAsync<ThrowingKeepAliveViewModel>("StackRegion");
        var throwing = (ThrowingKeepAliveViewModel)navService.GetCurrentViewModel("StackRegion")!;
        await navService.NavigateToAsync<KeepAliveViewModel>("StackRegion");
        var stacked = (KeepAliveViewModel)navService.GetCurrentViewModel("StackRegion")!;

        var ex = Assert.Throws<InvalidOperationException>(() => navService.Dispose());

        Assert.Equal("Sync dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        Assert.True(stacked.Disposed);
    }
    #region Page-level service scope (v2.0)

    [Fact]
    public async Task NavigateToAsync_WhenNavigatingToSameViewModel_ShouldReturnTrue_WithoutPushing()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.True(result);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task SingletonViewModel_WhenNavigatedAway_ShouldNeverBeDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<SingletonViewModel>();
        var singleton = (SingletonViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.False(singleton.Disposed);
        Assert.True(await navigationService.GoBackAsync());
        Assert.Same(singleton, navigationService.GetCurrentViewModel());
        Assert.False(singleton.Disposed);

        // Root-owned: neither a page scope nor the service may dispose it.
        await navigationService.DisposeAsync();
        Assert.False(singleton.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task NavigateToAsync_WhenSingletonDeeperOnStack_PopsBackToExistingInstance()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var inner = services.BuildServiceProvider();
        var tracking = new TrackingScopeFactory(inner.GetRequiredService<IServiceScopeFactory>());
        var navigationService = new NavigationService(new TestServiceProvider(inner, tracking));

        await navigationService.NavigateToAsync<SingletonViewModel>();
        var singleton = (SingletonViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<TestHomeViewModel>();

        var result = await navigationService.NavigateToAsync<SingletonViewModel>();

        // Mainstream behavior: pop back to the shared instance instead of fail-fast.
        Assert.True(result);
        Assert.Same(singleton, navigationService.GetCurrentViewModel());
        Assert.False(navigationService.CanGoBack());
        Assert.False(singleton.Disposed);

        // Three scopes were created (singleton page, home page, abandoned re-resolution);
        // the abandoned one and the popped home page's scope were released: no leak.
        Assert.Equal(3, tracking.CreatedScopes);
        Assert.Equal(2, tracking.DisposedScopes);

        await navigationService.DisposeAsync();
        Assert.Equal(3, tracking.DisposedScopes);
    }

    [Fact]
    public async Task NavigateToAsync_WhenTargetIsAlreadyActive_ShouldBeNoOp_WithoutResolvingDuplicate()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        var inner = services.BuildServiceProvider();
        var tracking = new TrackingScopeFactory(inner.GetRequiredService<IServiceScopeFactory>());
        var navigationService = new NavigationService(new TestServiceProvider(inner, tracking));

        Assert.True(await navigationService.NavigateToAsync<TestHomeViewModel>());
        var first = navigationService.GetCurrentViewModel();
        Assert.True(await navigationService.NavigateToAsync<TestHomeViewModel>());
        var second = navigationService.GetCurrentViewModel();

        Assert.Same(first, second);
        Assert.False(navigationService.CanGoBack());
        // One scope for the page; the no-op creates no scope at all (no duplicate
        // ViewModel is resolved and discarded).
        Assert.Equal(1, tracking.CreatedScopes);
        Assert.Equal(0, tracking.DisposedScopes);

        await navigationService.DisposeAsync();
        Assert.Equal(1, tracking.DisposedScopes);
    }

    [Fact]
    public async Task TransientDependencyGraph_ShouldBeDisposedWhenPageIsPopped()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<DisposableService>();
        services.AddTransient<ServiceDependentViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<ServiceDependentViewModel>();
        var viewModel = (ServiceDependentViewModel)navigationService.GetCurrentViewModel()!;
        Assert.False(viewModel.Service.Disposed);

        await navigationService.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace);

        // The page scope owns the whole dependency graph, so the injected service
        // is released together with the page's ViewModel.
        Assert.True(viewModel.Disposed);
        Assert.True(viewModel.Service.Disposed);

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ScopedViewModel_ShouldHaveOneInstancePerPage_WithIndependentDisposal()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddScoped<ScopedViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<ScopedViewModel>();
        var first = (ScopedViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<TestHomeViewModel>();
        await navigationService.NavigateToAsync<ScopedViewModel>();
        var second = (ScopedViewModel)navigationService.GetCurrentViewModel()!;

        // Each page gets its own scope, so each Scoped registration resolves once per page.
        Assert.NotEqual(first.InstanceId, second.InstanceId);

        Assert.True(await navigationService.GoBackAsync());
        Assert.True(second.Disposed);
        Assert.False(first.Disposed);
        Assert.True(navigationService.IsActive<TestHomeViewModel>());

        Assert.True(await navigationService.GoBackAsync());
        Assert.Same(first, navigationService.GetCurrentViewModel());

        await navigationService.DisposeAsync();
        Assert.True(first.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task PageScopeDisposal_WhenViewModelThrows_ShouldAbortThatScope_ButIsolateAcrossPages()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<DisposableService>();
        services.AddTransient<ThrowingDependencyViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<ThrowingDependencyViewModel>();
        var throwing = (ThrowingDependencyViewModel)navigationService.GetCurrentViewModel()!;
        await navigationService.NavigateToAsync<KeepAliveViewModel>();
        var healthy = (KeepAliveViewModel)navigationService.GetCurrentViewModel()!;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            navigationService.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Reset));

        Assert.Equal("VM dispose failed.", ex.Message);
        Assert.True(throwing.DisposeAttempted);
        // Pinned platform behavior: IServiceScope.Dispose stops at the first throwing
        // disposable, so the dependency created before the ViewModel is never released.
        Assert.False(throwing.Service.Disposed);
        // Cross-page isolation: the healthy page's scope was still disposed.
        Assert.True(healthy.Disposed);

        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ClearCache_WithSingletonKeepAlive_ShouldNotDisposeSingleton_AndAllowRenavigation()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<KeepAliveViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var first = (KeepAliveViewModel)navigationService.GetCurrentViewModel()!;

        await navigationService.ClearCacheAsync("MainRegion");
        Assert.False(first.Disposed);

        // Renavigation creates a fresh page scope around the same singleton instance.
        await navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        Assert.Same(first, navigationService.GetCurrentViewModel());
        Assert.False(first.Disposed);

        await navigationService.DisposeAsync();
        Assert.False(first.Disposed);
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ClearCache_Sync_WithAsyncOnlyViewModel_ShouldThrowInvalidOperationException_PinPlatformBehavior()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestDetailViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        await navigationService.NavigateToAsync<TestHomeViewModel>();
        await navigationService.NavigateToAsync<TestDetailViewModel, string>("Arg", "MainRegion", retention: NavigationRetention.KeepAlive);
        await navigationService.GoBackAsync();

        // TestDetailViewModel only implements IAsyncDisposable and now sits in the
        // KeepAlive cache. The DI container refuses synchronous disposal of such
        // scopes; the platform exception surfaces as-is so the caller knows to use
        // the asynchronous APIs.
        var ex = Assert.Throws<InvalidOperationException>(() => navigationService.ClearCache("MainRegion"));
        Assert.Contains("IAsyncDisposable", ex.Message);

        // The asynchronous path handles the same page cleanly.
        await navigationService.ClearCacheAsync("MainRegion");

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentNavigationsAndClearCache_ShouldNotLeakScopes_OrThrow()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        services.AddTransient<KeepAliveViewModel>();
        var inner = services.BuildServiceProvider();
        var tracking = new TrackingScopeFactory(inner.GetRequiredService<IServiceScopeFactory>());
        var navigationService = new NavigationService(new TestServiceProvider(inner, tracking));

        var tasks = new List<Task>();
        for (int worker = 0; worker < 8; worker++)
        {
            int capturedWorker = worker;
            tasks.Add(Task.Run(async () =>
            {
                string region = capturedWorker % 2 == 0 ? "R1" : "R2";
                for (int i = 0; i < 50; i++)
                {
                    switch ((i + capturedWorker) % 4)
                    {
                        case 0:
                            await navigationService.NavigateToAsync<TestHomeViewModel>(region);
                            break;
                        case 1:
                            await navigationService.NavigateToAsync<TestDetailViewModel>(region);
                            break;
                        case 2:
                            await navigationService.NavigateToAsync<KeepAliveViewModel>(region, retention: NavigationRetention.KeepAlive);
                            break;
                        default:
                            await navigationService.ClearCacheAsync(region);
                            break;
                    }

                    await navigationService.GoBackAsync(region);
                }
            }));
        }

        await Task.WhenAll(tasks);
        await navigationService.DisposeAsync();

        // Every page scope ever created was released exactly once: nothing leaked,
        // even with ClearCache racing navigation (stale cache entries are re-validated).
        Assert.Equal(tracking.CreatedScopes, tracking.DisposedScopes);
        Assert.True(tracking.CreatedScopes > 0);
    }

    #endregion

    #region Review feedback: cancellation, async lifecycle, refresh, guard context, thin base

    [Fact]
    public async Task NavigateToAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _navigationService.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token));

        Assert.Null(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task NavigateToAsync_WhenCancelledDuringResolve_ReleasesAbandonedScopeAndThrows()
    {
        TrackedDisposeViewModel.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TrackedDisposeViewModel>();
        var innerProvider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource();
        var provider = new TestServiceProvider(
            innerProvider,
            new CancellingScopeFactory(innerProvider.GetRequiredService<IServiceScopeFactory>(), cts));
        var navigationService = new NavigationService(provider);

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => navigationService.NavigateToAsync<TrackedDisposeViewModel>(cancellationToken: cts.Token));

        Assert.Equal(cts.Token, ex.CancellationToken);
        // The scope created for the abandoned page was released: nothing leaked.
        Assert.Equal(1, TrackedDisposeViewModel.DisposeCount);

        await navigationService.DisposeAsync();
        await innerProvider.DisposeAsync();
    }

    [Fact]
    public async Task NavigateToAsync_WhenCancelledAfterStackUpdate_RunsToCompletion()
    {
        using var cts = new CancellationTokenSource();
        CancelInNavigatedToViewModel.TokenSource = cts;
        try
        {
            await _navigationService.NavigateToAsync<TestHomeViewModel>();
            var result = await _navigationService.NavigateToAsync<CancelInNavigatedToViewModel>(
                cancellationToken: cts.Token);

            Assert.True(result);
            Assert.True(_navigationService.IsActive<CancelInNavigatedToViewModel>());
        }
        finally
        {
            CancelInNavigatedToViewModel.TokenSource = null;
        }
    }

    [Fact]
    public async Task GoBackAsync_WhenTokenAlreadyCancelled_ThrowsAndKeepsStack()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("x");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _navigationService.GoBackAsync(cancellationToken: cts.Token));

        Assert.True(_navigationService.IsActive<TestDetailViewModel>());
    }

    [Fact]
    public async Task INavigationAwareAsync_TakesPrecedenceOverSyncCallbacks()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<BothAwareViewModel>();
        var vm = (BothAwareViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.True(vm.AsyncToCalled);
        Assert.False(vm.SyncToCalled);

        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("x");

        Assert.True(vm.AsyncFromCalled);
        Assert.False(vm.SyncFromCalled);
    }

    [Fact]
    public async Task INavigationAwareAsync_WhenCallbackThrows_ExceptionPropagatesAfterTransition()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _navigationService.NavigateToAsync<AsyncThrowingViewModel>());

        Assert.Equal("OnNavigatedToAsync failed.", ex.Message);
        // The page was committed before the callback ran.
        Assert.True(_navigationService.IsActive<AsyncThrowingViewModel>());
    }

    [Fact]
    public async Task INavigationAwareAsync_Typed_ReceivesStronglyTypedParameter()
    {
        await _navigationService.NavigateToAsync<TypedAsyncViewModel, string>("typed-param");
        var vm = (TypedAsyncViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.Equal("typed-param", vm.ReceivedParam);
    }

    [Fact]
    public async Task NavigateToAsync_RefreshIfActive_ReinvokesCallbacksOnSameInstance()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        int navigatedEvents = 0;
        _navigationService.RegionNavigated += (_, _) => navigatedEvents++;

        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "second", refreshIfActive: true);

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, vm.NavigatedToCount);
        Assert.Equal("second", vm.LastParameter);
        Assert.Equal(1, navigatedEvents);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_SameTypeWithoutRefresh_IsSilentNoOp()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        int navigatedEvents = 0;
        _navigationService.RegionNavigated += (_, _) => navigatedEvents++;

        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>("second");

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(1, vm.NavigatedToCount);
        Assert.Equal(0, navigatedEvents);
    }

    [Fact]
    public async Task NavigateToAsync_RefreshIfActive_WhenNotOnTop_NavigatesNormally()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "p", refreshIfActive: true);

        Assert.True(result);
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Equal(1, vm.NavigatedToCount);
        Assert.Equal("p", vm.LastParameter);
        Assert.True(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_Refresh_PersistsNewParameterForGoBack()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var vm = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("second", refreshIfActive: true);
        Assert.Equal("second", vm.LastParameter);

        await _navigationService.NavigateToAsync<TestDetailViewModel, string>("detail");
        Assert.True(_navigationService.CanGoBack());

        await _navigationService.GoBackAsync();

        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        // The refreshed parameter was persisted on the stack entry, so GoBack
        // re-activates with "second", not the original "first".
        Assert.Equal("second", vm.LastParameter);
    }

    [Fact]
    public async Task NavigateToAsync_Refresh_DoesNotResolveTemporaryViewModel()
    {
        ConstructionCountingViewModel.ConstructionCount = 0;

        await _navigationService.NavigateToAsync<ConstructionCountingViewModel>();
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        var vm = (ConstructionCountingViewModel)_navigationService.GetCurrentViewModel()!;

        // Refresh must reuse the active instance without creating (and discarding)
        // a temporary ViewModel: no extra construction, no extra disposal.
        var result = await _navigationService.NavigateToAsync<ConstructionCountingViewModel>(
            refreshIfActive: true);

        Assert.True(result);
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, vm.NavigatedToCount);
    }

    [Fact]
    public async Task NavigateToAsync_SameTypeNoOp_DoesNotResolveTemporaryViewModel()
    {
        ConstructionCountingViewModel.ConstructionCount = 0;

        await _navigationService.NavigateToAsync<ConstructionCountingViewModel>();
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        var vm = (ConstructionCountingViewModel)_navigationService.GetCurrentViewModel()!;

        // Silent no-op: same instance, no callbacks, and no temporary resolution.
        var result = await _navigationService.NavigateToAsync<ConstructionCountingViewModel>();

        Assert.True(result);
        Assert.Equal(1, ConstructionCountingViewModel.ConstructionCount);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal(1, vm.NavigatedToCount);
    }

    [Fact]
    public async Task NavigateToAsync_Replace_ToActiveType_ReplacesEntry()
    {
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var first = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;

        // Explicit Replace takes precedence over the same-type no-op.
        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "second", operation: NavigationOperation.Replace);

        Assert.True(result);
        var second = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.Equal("second", second.LastParameter);
        Assert.Equal(1, second.NavigatedToCount);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_ClearStack_ToActiveType_ClearsStackAndPushesFresh()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<RefreshableViewModel, string>("first");
        var first = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(_navigationService.CanGoBack());

        // Explicit ClearStack takes precedence over the same-type no-op.
        var result = await _navigationService.NavigateToAsync<RefreshableViewModel, string>(
            "second", operation: NavigationOperation.Reset);

        Assert.True(result);
        var second = (RefreshableViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.Equal("second", second.LastParameter);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_KeepAlive_ToActiveType_WithSameParameter_ReactivatesInPlace()
    {
        await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", retention: NavigationRetention.KeepAlive);
        var vm = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Equal("first", vm.LastParameter);

        // The active instance is recognized by its parameter and reactivated
        // in place — no new instance, no stack growth.
        var result = await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.Equal("first", vm.LastParameter);
        Assert.False(vm.Disposed);
        Assert.False(_navigationService.CanGoBack());
    }

    [Fact]
    public async Task NavigateToAsync_KeepAlive_ToActiveType_WithDifferentParameter_PushesNewInstance()
    {
        await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "first", retention: NavigationRetention.KeepAlive);
        var first = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.Equal("first", first.LastParameter);

        // The active instance represents a different parameter than the one
        // addressed, so it is not re-purposed: a new instance is pushed and
        // the first one stays alive underneath.
        var result = await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "second", retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        var second = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.Equal("second", second.LastParameter);
        Assert.Equal("first", first.LastParameter);
        Assert.False(first.Disposed);
        Assert.True(_navigationService.CanGoBack());

        Assert.True(await _navigationService.GoBackAsync());
        Assert.Same(first, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task INavigationGuardWithContext_ReceivesFullContext()
    {
        await _navigationService.NavigateToAsync<ContextGuardViewModel>();
        var guard = (ContextGuardViewModel)_navigationService.GetCurrentViewModel()!;

        var result = await _navigationService.NavigateToAsync<TestDetailViewModel, string>("p1");

        Assert.True(result);
        var ctx = guard.LastContext!;
        Assert.Equal("MainRegion", ctx.RegionName);
        Assert.Equal(typeof(TestDetailViewModel), ctx.TargetViewModelType);
        Assert.Equal(NavigationOperation.Push, ctx.Operation);
        Assert.Equal(NavigationRetention.Destroy, ctx.Retention);
        Assert.Equal("p1", ctx.Parameter);
        Assert.False(ctx.IsBack);
    }

    [Fact]
    public async Task INavigationGuardWithContext_Deny_CancelsNavigation()
    {
        await _navigationService.NavigateToAsync<ContextGuardViewModel>();
        var guard = (ContextGuardViewModel)_navigationService.GetCurrentViewModel()!;
        guard.Allow = false;

        var result = await _navigationService.NavigateToAsync<TestDetailViewModel>();

        Assert.False(result);
        Assert.Same(guard, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task INavigationGuardWithContext_TakesPrecedenceOverPlainGuard()
    {
        await _navigationService.NavigateToAsync<BothGuardViewModel>();
        var guard = (BothGuardViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>());

        Assert.True(guard.ContextGuardCalled);
        Assert.False(guard.PlainGuardCalled);
    }

    [Fact]
    public async Task GoBack_GuardWithContext_ReceivesBackContext()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel, string>("home-param");
        await _navigationService.NavigateToAsync<ContextGuardDetailViewModel>();
        var guard = (ContextGuardDetailViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.True(await _navigationService.GoBackAsync());

        var ctx = guard.LastContext!;
        Assert.True(ctx.IsBack);
        Assert.Null(ctx.Operation);
        Assert.Null(ctx.Retention);
        Assert.Equal(typeof(TestHomeViewModel), ctx.TargetViewModelType);
        Assert.Equal("home-param", ctx.Parameter);
        Assert.Equal("MainRegion", ctx.RegionName);
    }

    [Fact]
    public async Task NavigationViewModelBase_ThinBase_WorksWithoutToolkit()
    {
        await _navigationService.NavigateToAsync<ThinViewModel>();
        var thin = (ThinViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.NotNull(thin.Navigation);

        int changed = 0;
        thin.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThinViewModel.Title))
            {
                changed++;
            }
        };
        thin.Title = "hello";
        thin.Title = "hello"; // unchanged value: no event

        Assert.Equal(1, changed);

        Assert.True(await thin.GoToDetailAsync());
        Assert.True(_navigationService.IsActive<TestDetailViewModel>());
        Assert.Null(thin.Navigation);
    }

    [Fact]
    public async Task ViewModelReleased_FiresForSingletonPageTeardown_ButInstanceSurvives()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = (NavigationService)serviceProvider.GetRequiredService<INavigationService>();

        INavigationViewModel? disposedVm = null;
        navigationService.ViewModelReleased += (_, vm) => disposedVm = vm;

        await navigationService.NavigateToAsync<SingletonViewModel>();
        var singleton = (SingletonViewModel)navigationService.GetCurrentViewModel()!;
        // Replace destroys the singleton's page (a merely covered page in New mode stays
        // alive on the back stack and must NOT raise ViewModelReleased).
        await navigationService.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace);

        // Ownership was released (event fired) but the root-owned instance survives.
        Assert.Same(singleton, disposedVm);
        Assert.False(singleton.Disposed);

        await navigationService.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    #endregion

    #region v2.0 test doubles

    private sealed class RefreshableViewModel : NavigationViewModelBase, INavigationAware
    {
        public int NavigatedToCount { get; private set; }
        public object? LastParameter { get; private set; }

        public void OnNavigatedTo(object? parameter)
        {
            NavigatedToCount++;
            LastParameter = parameter;
        }

        public void OnNavigatedFrom() { }
    }

    private sealed class ConstructionCountingViewModel : NavigationViewModelBase, INavigationAware
    {
        public static int ConstructionCount;

        public ConstructionCountingViewModel()
        {
            Interlocked.Increment(ref ConstructionCount);
        }

        public int NavigatedToCount { get; private set; }

        public void OnNavigatedTo(object? parameter) => NavigatedToCount++;

        public void OnNavigatedFrom() { }
    }

    private sealed class BothAwareViewModel : NavigationViewModelBase, INavigationAware, INavigationAwareAsync
    {
        public bool SyncToCalled { get; private set; }
        public bool SyncFromCalled { get; private set; }
        public bool AsyncToCalled { get; private set; }
        public bool AsyncFromCalled { get; private set; }

        public void OnNavigatedTo(object? parameter) => SyncToCalled = true;
        public void OnNavigatedFrom() => SyncFromCalled = true;

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            AsyncToCalled = true;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            AsyncFromCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class SlowInitViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public TaskCompletionSource<bool> InitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> InitCanComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ReceivedToken { get; private set; }
        public bool TokenWasCancelled { get; private set; }
        /// <summary>Simulates slow cleanup after observing cancellation.</summary>
        public bool SimulateSlowCleanup { get; set; }

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            ReceivedToken = cancellationToken;
            InitStarted.TrySetResult(true);
            try
            {
                await InitCanComplete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TokenWasCancelled = true;
                if (SimulateSlowCleanup)
                {
                    // Non-cancellable cleanup: disposal gating must wait for this.
                    CleanupStarted.TrySetResult(true);
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                throw;
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TokenCapturingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public CancellationToken LastToken { get; private set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SelfReplacingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Navigation is not null)
            {
                await Navigation.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace).ConfigureAwait(false);
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RefreshTrackingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public CancellationToken LastToken { get; private set; }
        public TaskCompletionSource? BlockActivation { get; set; }

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            if (BlockActivation is not null)
            {
                var gate = BlockActivation;
                BlockActivation = null; // Only block once.
                await gate.Task.ConfigureAwait(false);
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CancellableViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public CancellationToken Token { get; private set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DisposableTracker : IDisposable
    {
        private bool _disposed;
        public bool UsedAfterReplace { get; private set; }
        public void MarkUsed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DisposableTracker));
            UsedAfterReplace = true;
        }
        public void Dispose() => _disposed = true;
    }

    private sealed class SyncSelfReplacingAndContinuingViewModel(DisposableTracker tracker) : NavigationViewModelBase, INavigationAware
    {
        public void OnNavigatedTo(object? parameter)
        {
            if (Navigation is not null)
            {
                // Reentrant self-destroying navigation, blocked on synchronously
                // because this callback cannot await...
                Navigation.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace).GetAwaiter().GetResult();
                // ...then continue using scope services. Throws if scope was disposed.
                tracker.MarkUsed();
            }
        }

        public void OnNavigatedFrom()
        {
        }
    }

    private sealed class SelfReplacingAndContinuingViewModel(DisposableTracker tracker) : NavigationViewModelBase, INavigationAwareAsync
    {
        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Navigation is not null)
            {
                // Reentrant self-destroying navigation...
                await Navigation.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace).ConfigureAwait(false);
                // ...then continue using scope services. Throws if scope was disposed.
                tracker.MarkUsed();
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class SlowLeaveViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public TaskCompletionSource? LeaveGate { get; set; }
        public bool LeaveCompleted { get; private set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            if (LeaveGate is not null)
            {
                await LeaveGate.Task.ConfigureAwait(false);
            }
            LeaveCompleted = true;
        }
    }

    private sealed class ReentrantAsyncViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Navigation is not null)
            {
                await Navigation.NavigateToAsync<TestHomeViewModel>().ConfigureAwait(false);
            }
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HangingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public TaskCompletionSource<bool> InitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            InitStarted.TrySetResult(true);
            // Ignores cancellation entirely: hangs forever.
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AsyncThrowingViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("OnNavigatedToAsync failed."));

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TypedAsyncViewModel : NavigationViewModelBase, INavigationAwareAsync<string>
    {
        public string? ReceivedParam { get; private set; }

        public Task OnNavigatedToAsync(string parameter, CancellationToken cancellationToken)
        {
            ReceivedParam = parameter;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ContextGuardViewModel : NavigationViewModelBase, INavigationGuardWithContext
    {
        public NavigationGuardContext? LastContext { get; private set; }
        public int CallCount { get; private set; }
        public bool Allow { get; set; } = true;

        public Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            CallCount++;
            return Task.FromResult(Allow);
        }
    }

    private sealed class BothGuardViewModel : NavigationViewModelBase, INavigationGuard, INavigationGuardWithContext
    {
        public bool PlainGuardCalled { get; private set; }
        public bool ContextGuardCalled { get; private set; }

        public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            PlainGuardCalled = true;
            return Task.FromResult(true);
        }

        public Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken)
        {
            ContextGuardCalled = true;
            return Task.FromResult(true);
        }
    }

    private sealed class ContextGuardDetailViewModel : NavigationViewModelBase, INavigationGuardWithContext
    {
        public NavigationGuardContext? LastContext { get; private set; }

        public Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(true);
        }
    }

    private sealed class CancelInNavigatedToViewModel : NavigationViewModelBase, INavigationAware
    {
        public static CancellationTokenSource? TokenSource;

        public void OnNavigatedTo(object? parameter) => TokenSource?.Cancel();
        public void OnNavigatedFrom() { }
    }

    private sealed class ThinViewModel : NavigationViewModelBase
    {
        private string? _title;

        public string? Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public Task<bool> GoToDetailAsync() => NavigateToAsync<TestDetailViewModel>();
    }

    /// <summary>
    /// Cancels the token on the first created scope, simulating cancellation that lands
    /// between page-scope creation and the stack update.
    /// </summary>
    private sealed class CancellingScopeFactory(IServiceScopeFactory inner, CancellationTokenSource cts) : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner = inner;
        private readonly CancellationTokenSource _cts = cts;
        private int _calls;

        public IServiceScope CreateScope()
        {
            var scope = _inner.CreateScope();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _cts.Cancel();
            }

            return scope;
        }
    }

    private sealed class SingletonViewModel : NavigationViewModelBase, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class DisposableService : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ServiceDependentViewModel : NavigationViewModelBase, IDisposable
    {
        public DisposableService Service { get; }
        public bool Disposed { get; private set; }

        public ServiceDependentViewModel(DisposableService service)
        {
            Service = service;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class ScopedViewModel : NavigationViewModelBase, IDisposable
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingDependencyViewModel : NavigationViewModelBase, IDisposable
    {
        public DisposableService Service { get; }
        public bool DisposeAttempted { get; private set; }

        public ThrowingDependencyViewModel(DisposableService service)
        {
            Service = service;
        }

        public void Dispose()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("VM dispose failed.");
        }
    }

    /// <summary>
    /// Wraps an <see cref="IServiceScopeFactory"/> and counts created and disposed page scopes.
    /// </summary>
    private sealed class TrackingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner = inner;
        public int CreatedScopes;
        public int DisposedScopes;

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref CreatedScopes);
            return new TrackingScope(_inner.CreateScope(), this);
        }

        private sealed class TrackingScope(IServiceScope inner, TrackingScopeFactory factory) : IServiceScope, IAsyncDisposable
        {
            private readonly IServiceScope _inner = inner;
            private readonly TrackingScopeFactory _factory = factory;

            public IServiceProvider ServiceProvider => _inner.ServiceProvider;

            public void Dispose()
            {
                _inner.Dispose();
                Interlocked.Increment(ref _factory.DisposedScopes);
            }

            public async ValueTask DisposeAsync()
            {
                if (_inner is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    _inner.Dispose();
                }

                Interlocked.Increment(ref _factory.DisposedScopes);
            }
        }
    }

    /// <summary>
    /// Forwards <see cref="IServiceProvider"/> lookups to an inner provider, but substitutes
    /// a custom <see cref="IServiceScopeFactory"/>.
    /// </summary>
    private sealed class TestServiceProvider(IServiceProvider inner, IServiceScopeFactory scopeFactory) : IServiceProvider
    {
        private readonly IServiceProvider _inner = inner;
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? _scopeFactory : _inner.GetService(serviceType);
    }

    #endregion

    #region Review Regression Tests (P1/P2 fixes)

    [Fact]
    public async Task ClearStack_ShouldNotRepeatNavigatedFrom()
    {
        // A -> B -> C, then ClearStack to D.
        // A already received OnNavigatedFrom when B covered it; ClearStack must not
        // notify it again. Only C (the active page) gets OnNavigatedFrom here.
        // Note: A and C share the type but are different instances (B is a different
        // type in between, so no same-type no-op applies).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<CountingAwareViewModel>();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<CountingAwareViewModel>(); // A (instance 1)
        var a = (CountingAwareViewModel)nav.GetCurrentViewModel()!;
        await nav.NavigateToAsync<TestHomeViewModel>(); // B covers A
        await nav.NavigateToAsync<CountingAwareViewModel>(); // C (instance 2) covers B
        var c = (CountingAwareViewModel)nav.GetCurrentViewModel()!;
        Assert.NotSame(a, c);

        Assert.Equal(1, a.NavigatedFromCount); // covered by B
        Assert.Equal(0, c.NavigatedFromCount); // active

        await nav.NavigateToAsync<TestDetailViewModel>(operation: NavigationOperation.Reset);

        Assert.Equal(1, a.NavigatedFromCount); // must NOT be notified again
        Assert.Equal(1, c.NavigatedFromCount); // active page: exactly once
    }

    [Fact]
    public async Task PopToExisting_ShouldNotRepeatNavigatedFrom()
    {
        // A(KeepAlive) -> B -> back to A via KeepAlive (pop-to-existing).
        // B was active when popped, so it gets OnNavigatedFrom once.
        // A was already inactive (covered by B); popping B must not notify A again.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<CountingAwareViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<CountingAwareViewModel>(retention: NavigationRetention.KeepAlive); // A
        var a = (CountingAwareViewModel)nav.GetCurrentViewModel()!;
        await nav.NavigateToAsync<TestHomeViewModel>(); // B covers A
        Assert.Equal(1, a.NavigatedFromCount);

        await nav.NavigateToAsync<CountingAwareViewModel>(retention: NavigationRetention.KeepAlive); // pop back to A
        Assert.Same(a, nav.GetCurrentViewModel());
        Assert.Equal(1, a.NavigatedFromCount); // must NOT be notified again
        Assert.Equal(2, a.NavigatedToCount);   // reactivated
    }

    [Fact]
    public async Task ReentrantNavigation_ShouldSuppressStaleRegionNavigatedEvent()
    {
        // ReentrantNavigatedToViewModel navigates to TestHomeViewModel inside OnNavigatedTo.
        // The outer transition's RegionNavigated(Reentrant) event must be suppressed;
        // only RegionNavigated(Home) should fire, and the final page must be Home.
        var events = new List<string>();
        _navigationService.RegionNavigated += (_, vm) => events.Add(vm.GetType().Name);

        await _navigationService.NavigateToAsync<ReentrantNavigatedToViewModel>();

        // The redirect registered in OnNavigatedTo executes right after
        // the initiating call completes; RegionNavigated for the final
        // page fires when it comes to rest — wait for both before
        // asserting the final state.
        await WaitUntilAsync(() => events.Contains(nameof(TestHomeViewModel)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        Assert.DoesNotContain(nameof(ReentrantNavigatedToViewModel), events);
        Assert.Equal(new[] { nameof(TestHomeViewModel) }, events);
    }

    [Fact]
    public async Task SameTypeNoOp_ShouldNotBeVetoedByGuard()
    {
        // Navigating to the already-active type (New mode, no refresh) is a no-op
        // and must not be vetoed by the leave guard.
        await _navigationService.NavigateToAsync<GuardedViewModel>();
        var guarded = (GuardedViewModel)_navigationService.GetCurrentViewModel()!;
        guarded.AllowNavigation = false;

        var result = await _navigationService.NavigateToAsync<GuardedViewModel>();

        Assert.True(result);
        Assert.Same(guarded, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task SubscriberError_ShouldBeReportedWithoutBreakingNavigation()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += ex => errors.Add(ex);
        _navigationService.RegionNavigated += (_, _) => throw new InvalidOperationException("boom");

        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.True(result);
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task SubscriberError_ThrowingSubscriber_DoesNotStarveLaterSubscribers()
    {
        var received = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.SubscriberError += _ => throw new InvalidOperationException("handler boom");
        _navigationService.SubscriberError += ex => received.TrySetResult(ex);
        _navigationService.RegionNavigated += (_, _) => throw new InvalidOperationException("boom");

        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.True(result);
        // The RegionNavigated handler's failure is forwarded to
        // SubscriberError: the first error subscriber throws, and the
        // second must still receive the same error.
        var error = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public async Task DefinitiveCurrentPageQuery_AnswersDefinitiveStates()
    {
        var query = (IDefinitiveCurrentPageQuery)_navigationService;

        // No navigation yet: definitively none.
        Assert.Null(query.GetDefinitiveCurrentViewModel("MainRegion"));

        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        Assert.Same(
            _navigationService.GetCurrentViewModel(),
            query.GetDefinitiveCurrentViewModel("MainRegion"));

        // A region that never had pages: definitively none.
        Assert.Null(query.GetDefinitiveCurrentViewModel("OtherRegion"));

        // After disposal every stack is cleared: definitively none again.
        ((NavigationService)_navigationService).Dispose();
        Assert.Null(query.GetDefinitiveCurrentViewModel("MainRegion"));
    }

    [Fact]
    public async Task ClearStack_ToSingletonAlreadyOnStack_ShouldActuallyClear()
    {
        // Home -> Settings(singleton) -> Detail, then ClearStack to Settings.
        // The resolved Settings instance is the same object already on the stack;
        // ClearStack must still empty the stack instead of popping back to it.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SingletonViewModel>();
        var settings = nav.GetCurrentViewModel();
        await nav.NavigateToAsync<TestDetailViewModel>();
        Assert.True(nav.CanGoBack());

        var result = await nav.NavigateToAsync<SingletonViewModel>(operation: NavigationOperation.Reset);

        Assert.True(result);
        Assert.Same(settings, nav.GetCurrentViewModel());
        Assert.False(nav.CanGoBack()); // stack holds only Settings
    }

    [Fact]
    public async Task Replace_ToSingletonAlreadyOnStack_ShouldNotDuplicate()
    {
        // Home -> Settings(singleton) -> Detail, then Replace top with Settings.
        // The deeper Settings entry must be removed so the singleton appears once.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddSingleton<SingletonViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SingletonViewModel>();
        var settings = nav.GetCurrentViewModel();
        await nav.NavigateToAsync<TestDetailViewModel>();

        var result = await nav.NavigateToAsync<SingletonViewModel>(operation: NavigationOperation.Replace);

        Assert.True(result);
        Assert.Same(settings, nav.GetCurrentViewModel());
        // Home -> Settings: back once to Home, then nothing.
        Assert.True(nav.CanGoBack());
        Assert.True(await nav.GoBackAsync());
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        Assert.False(nav.CanGoBack());
    }

    [Fact]
    public async Task SlowInit_CancelledOnNavigateAway()
    {
        // B's OnNavigatedToAsync is still running when we navigate away: its token
        // must be cancelled so it stops touching the page's dependencies.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var navTask = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Navigate away while init is still in flight.
        var awayTask = nav.NavigateToAsync<TestHomeViewModel>();
        await awayTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Let the cancelled init observe the token.
        slowVm.InitCanComplete.TrySetResult(true);
        await navTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(slowVm.TokenWasCancelled);
        Assert.True(slowVm.ReceivedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Replace_WaitsForInFlightLifecycleBeforeDisposing()
    {
        // Replace B with C while B's OnNavigatedToAsync is in flight: the scope must
        // not be released until B's callback completes (disposal gating).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var navTask = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        slowVm.SimulateSlowCleanup = true;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var replaceTask = nav.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace);
        // Once B's slow cleanup is running, the replace is causally blocked
        // behind it: the disposal gate cannot release before the cleanup ends.
        await slowVm.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(replaceTask.IsCompleted); // gated: waiting for B's cleanup

        await replaceTask.WaitAsync(TimeSpan.FromSeconds(10));
        await navTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        Assert.True(slowVm.TokenWasCancelled);
    }

    [Fact]
    public async Task HangingLifecycle_DisposalTimesOutAndNavigationCompletes()
    {
        // A ViewModel that ignores its cancellation token must not block navigation
        // forever: after the disposal gate timeout, the scope is released anyway.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<HangingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var errors = new List<Exception>();
        nav.SubscriberError += ex => errors.Add(ex);

        _ = nav.NavigateToAsync<HangingViewModel>();
        var hangingVm = (HangingViewModel)nav.GetCurrentViewModel()!;
        await hangingVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Replace: disposal gate waits 5s for the hanging init, then times out.
        var replaceTask = nav.NavigateToAsync<TestHomeViewModel>(operation: NavigationOperation.Replace);
        await replaceTask.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
        var timeoutError = Assert.Single(errors, e => e is TimeoutException);
        Assert.Contains("Disposal gate timed out", timeoutError.Message);
    }

    [Fact]
    public async Task CoveredPage_CancelledOnCover()
    {
        // A covered by B: A's init token must be cancelled when B covers it.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var navTask = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await nav.NavigateToAsync<TestHomeViewModel>().WaitAsync(TimeSpan.FromSeconds(5));

        slowVm.InitCanComplete.TrySetResult(true);
        await navTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(slowVm.TokenWasCancelled);
    }

    [Fact]
    public async Task ReentrantAsyncNavigation_NoDeadlock()
    {
        // Async OnNavigatedTo that re-enters navigation must not deadlock with the
        // disposal gating (no lock is held across the lifecycle).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantAsyncViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<ReentrantAsyncViewModel>().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result);
        // The initiating call completes when the reentrant page's window
        // closes; the redirect its callback registered executes right
        // afterwards on the pump — wait for the chain to settle before
        // asserting the final page.
        await WaitUntilAsync(() => nav.GetCurrentViewModel() is TestHomeViewModel);
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task GoBack_ReactivatesWithFreshToken()
    {
        // A -> B -> GoBack: A's OnNavigatedToAsync must receive a NON-cancelled token.
        // (Regression: PR #42 reused the entry whose CTS was cancelled when B covered A.)
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TokenCapturingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TokenCapturingViewModel>();
        var vm = (TokenCapturingViewModel)nav.GetCurrentViewModel()!;
        Assert.False(vm.LastToken.IsCancellationRequested); // first activation

        await nav.NavigateToAsync<TestHomeViewModel>(); // covers A, cancels its CTS
        Assert.True(vm.LastToken.IsCancellationRequested);

        await nav.GoBackAsync(); // reactivates A
        Assert.False(vm.LastToken.IsCancellationRequested); // fresh token!
        Assert.Same(vm, nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task DisposeAsync_WaitsForInFlightLifecycle()
    {
        // DisposeAsync must cancel in-flight callbacks and wait for them (with timeout)
        // before releasing scopes — not dispose out from under a running callback.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowInitViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        _ = nav.NavigateToAsync<SlowInitViewModel>();
        var slowVm = (SlowInitViewModel)nav.GetCurrentViewModel()!;
        slowVm.SimulateSlowCleanup = true;
        await slowVm.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeTask = ((IAsyncDisposable)nav).DisposeAsync().AsTask();
        await Task.Delay(300);
        Assert.False(disposeTask.IsCompleted); // waiting for slow cleanup

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(slowVm.TokenWasCancelled);
    }

    [Fact]
    public async Task ReentrantReplace_DoesNotSelfWait()
    {
        // A's OnNavigatedToAsync does Replace to B: the disposal gate must not wait
        // for A's own callback (self-wait would burn the 5s timeout).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SelfReplacingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        // The self-wait, if it recurs, happens in the Replace hop's
        // disposal of the replaced page — which runs AFTER the hop's stack
        // commit flips the current page. The observable that brackets it
        // is the page's release (its scope disposal), not the flip.
        var replacedReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        nav.ViewModelReleased += (_, vm) =>
        {
            if (vm is SelfReplacingViewModel)
            {
                replacedReleased.TrySetResult();
            }
        };

        var startTime = DateTime.UtcNow;
        var result = await nav.NavigateToAsync<SelfReplacingViewModel>().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result);
        // As in the reentrant-navigation test: the Replace the callback
        // registered executes right after the initiating call completes.
        await WaitUntilAsync(() => nav.GetCurrentViewModel() is TestHomeViewModel);
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());

        await replacedReleased.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var elapsed = DateTime.UtcNow - startTime;
        Assert.True(elapsed < TimeSpan.FromSeconds(4), $"Took {elapsed}, self-wait would take ~5s");
    }

    [Fact]
    public async Task RegionNavigated_ThrowingFirstSubscriber_ShouldNotBlockOthers()
    {
        // Multicast delegate semantics: one throwing subscriber must not prevent
        // later subscribers (e.g. the Host) from receiving the event.
        var errors = new List<Exception>();
        _navigationService.SubscriberError += ex => errors.Add(ex);
        _navigationService.RegionNavigated += (_, _) => throw new InvalidOperationException("first boom");

        string? secondRegion = null;
        INavigationViewModel? secondVm = null;
        _navigationService.RegionNavigated += (r, vm) => { secondRegion = r; secondVm = vm; };

        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>();

        Assert.True(result);
        Assert.Equal("MainRegion", secondRegion);
        Assert.IsType<TestHomeViewModel>(secondVm);
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task Refresh_CancelsOldActivationAndReusesEntry()
    {
        // Refresh must not orphan the old activation: the old token is cancelled,
        // the entry (and its in-flight tracking) is reused, and the new activation
        // gets a fresh token.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<RefreshTrackingViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<RefreshTrackingViewModel>();
        Assert.True(result);
        var vm = Assert.IsType<RefreshTrackingViewModel>(nav.GetCurrentViewModel());
        var firstToken = vm.LastToken;
        Assert.False(firstToken.IsCancellationRequested);

        // Block the refresh activation, then verify the old token was cancelled.
        var blockGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.BlockActivation = blockGate;
        var refreshTask = nav.NavigateToAsync<RefreshTrackingViewModel>(refreshIfActive: true);
        await Task.Delay(200); // Let the refresh activation start and block.

        Assert.True(firstToken.IsCancellationRequested, "Refresh must cancel the previous activation token");

        blockGate.SetResult();
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotEqual(firstToken, vm.LastToken);
        Assert.False(vm.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task SyncDispose_CancelsLifecycleTokens()
    {
        // Sync Dispose cannot wait for async callbacks, but it must cancel
        // their tokens before releasing scopes.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<CancellableViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<CancellableViewModel>();
        Assert.True(result);
        var vm = Assert.IsType<CancellableViewModel>(nav.GetCurrentViewModel());

        nav.Dispose();

        Assert.True(vm.Token.IsCancellationRequested, "Sync Dispose must cancel lifecycle tokens");
    }

    [Fact]
    public async Task ReentrantReplace_DefersScopeDisposalUntilCallbackExits()
    {
        // When a callback awaits a self-destroying navigation and then continues
        // using scope services, the scope must not be disposed until the callback
        // exits. DisposableTracker.MarkUsed throws if called after disposal.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SelfReplacingAndContinuingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        services.AddScoped<DisposableTracker>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        // If the scope were disposed immediately on Replace, MarkUsed would throw
        // ObjectDisposedException inside the callback, failing the navigation.
        var result = await nav.NavigateToAsync<SelfReplacingAndContinuingViewModel>().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        // The Replace the callback registered executes right after the
        // initiating call completes — wait for the chain to settle.
        await WaitUntilAsync(() => nav.GetCurrentViewModel() is TestHomeViewModel);
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task SyncReentrantReplace_DefersScopeDisposalUntilCallbackExits()
    {
        // Sync counterpart of ReentrantReplace_DefersScopeDisposalUntilCallbackExits:
        // a synchronous OnNavigatedTo that blocks on a self-destroying navigation
        // and then continues using scope services must see the same deferral.
        // Sync callbacks are tracked in-flight exactly like async ones.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncSelfReplacingAndContinuingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        services.AddScoped<DisposableTracker>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<SyncSelfReplacingAndContinuingViewModel>().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        // Same settle wait as the async counterpart.
        await WaitUntilAsync(() => nav.GetCurrentViewModel() is TestHomeViewModel);
        Assert.IsType<TestHomeViewModel>(nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task ClearCacheAsync_WaitsForInFlightLifecycle()
    {
        // ClearCacheAsync must not release a cached scope while its
        // OnNavigatedFromAsync is still running.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SlowLeaveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<SlowLeaveViewModel>(retention: NavigationRetention.KeepAlive);
        var vm = (SlowLeaveViewModel)nav.GetCurrentViewModel()!;
        vm.LeaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Navigate away (KeepAlive -> cached), leave callback starts and blocks.
        var away = nav.NavigateToAsync<TestHomeViewModel>();
        await Task.Delay(100);
        Assert.False(vm.LeaveCompleted);

        // Clear cache while leave is in-flight: should wait, not dispose early.
        var clear = nav.ClearCacheAsync("MainRegion");
        await Task.Delay(200);
        Assert.False(vm.LeaveCompleted, "ClearCacheAsync must wait for in-flight leave callback");

        vm.LeaveGate.SetResult();
        await away;
        await clear.AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.LeaveCompleted);
    }

    [Fact]
    public async Task Refresh_CancelCallbackMustNotDeadlock()
    {
        // A cancellation callback that reenters the navigation service must not
        // deadlock: Cancel() runs outside the navigation locks.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantCancelViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ReentrantCancelViewModel>();
        var vm = Assert.IsType<ReentrantCancelViewModel>(nav.GetCurrentViewModel());
        vm.EnableReentrantCancel(nav);
        var cts = GetActiveEntryCts(nav, "MainRegion");
        Assert.NotNull(cts);
        vm.RegisterReentrantCallback(cts.Token);

        // Refresh triggers BeginNewActivation -> Cancel() outside locks.
        // If Cancel ran while holding the lock, the reentrant ClearCache would deadlock.
        var result = await nav.NavigateToAsync<ReentrantCancelViewModel>(refreshIfActive: true)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        Assert.True(vm.CancelCallbackRan);
    }

    [Fact]
    public async Task ClearCache_DoesNotCancelActiveKeepAlivePageToken()
    {
        // Clearing the cache must not cancel the lifecycle token of a page
        // that is still active (on the navigation stack). Mutual exclusion:
        // the active page is not in the cache at all, so ClearCache cannot
        // reach it.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantCancelViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ReentrantCancelViewModel>(retention: NavigationRetention.KeepAlive);
        var vm = Assert.IsType<ReentrantCancelViewModel>(nav.GetCurrentViewModel());
        vm.EnableReentrantCancel(nav);
        var cts = GetActiveEntryCts(nav, "MainRegion");
        Assert.NotNull(cts);
        vm.RegisterReentrantCallback(cts.Token);

        // The page is still active: ClearCache must leave its token uncancelled.
        nav.ClearCache("MainRegion");
        Assert.False(cts.IsCancellationRequested);
        Assert.False(vm.CancelCallbackRan);
        Assert.IsType<ReentrantCancelViewModel>(nav.GetCurrentViewModel());
    }

    private static CancellationTokenSource? GetActiveEntryCts(
        INavigationService nav, string regionName)
    {
        // Test hook: reach the internal region stack to observe the active entry's token.
        var serviceType = nav.GetType();
        var stacksField = serviceType.GetField("_regionStacks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var stacks = stacksField?.GetValue(nav) as System.Collections.IDictionary;
        if (stacks is null || !stacks.Contains(regionName))
        {
            return null;
        }

        var stack = stacks[regionName] as System.Collections.IEnumerable;
        if (stack is null)
        {
            return null;
        }

        // Stack<T> enumerates LIFO; the first item is the top (active entry).
        foreach (var entry in stack)
        {
            var ctsProp = entry?.GetType().GetProperty("LifecycleCts");
            return ctsProp?.GetValue(entry) as CancellationTokenSource;
        }

        return null;
    }

    private static CancellationTokenSource? GetKeepAliveEntryCts(
        INavigationService nav, string regionName, Type viewModelType)
    {
        // Test hook: reach the internal KeepAlive cache entry to observe its token.
        var serviceType = nav.GetType();
        var cacheField = serviceType.GetField("_keepAliveCache",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = cacheField?.GetValue(nav) as System.Collections.IDictionary;
        if (cache is null)
        {
            return null;
        }

        foreach (System.Collections.DictionaryEntry kv in cache)
        {
            var key = kv.Key;
            // Key is ValueTuple<string, Type>: (Region, ViewModelType).
            // ValueTuple uses public fields, not properties.
            var keyType = key.GetType();
            var item1Field = keyType.GetField("Item1");
            var item2Field = keyType.GetField("Item2");
            var item1 = (string?)item1Field?.GetValue(key);
            var item2 = (Type?)item2Field?.GetValue(key);
            if (item1 == regionName && item2 == viewModelType)
            {
                var entry = kv.Value;
                var ctsProp = entry?.GetType().GetProperty("LifecycleCts");
                return ctsProp?.GetValue(entry) as CancellationTokenSource;
            }
        }

        return null;
    }

    [Fact]
    public async Task Dispose_CancelCallbackMustNotDeadlock()
    {
        // A cancel callback that reenters Dispose must not deadlock: sync Dispose
        // collects CTSs under the lock and cancels after release.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantCancelViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ReentrantCancelViewModel>();
        var vm = Assert.IsType<ReentrantCancelViewModel>(nav.GetCurrentViewModel());
        vm.EnableReentrantDispose(nav);
        var cts = GetActiveEntryCts(nav, "MainRegion");
        Assert.NotNull(cts);
        vm.RegisterReentrantCallback(cts.Token);

        // Dispose cancels the entry's token; the callback reenters Dispose.
        // If Cancel ran while holding _navigationLock, this would deadlock.
        var disposeTask = Task.Run(() => ((IDisposable)nav).Dispose());
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.CancelCallbackRan);
    }

    private sealed class ReentrantCancelViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        private INavigationService? _nav;
        private bool _reenterDispose;
        public bool CancelCallbackRan { get; private set; }

        public void EnableReentrantCancel(INavigationService nav) => _nav = nav;

        public void RegisterReentrantCallback(CancellationToken token)
        {
            token.Register(() =>
            {
                CancelCallbackRan = true;
                // This would deadlock if Cancel() ran while holding _navigationLock.
                if (_reenterDispose)
                {
                    ((IDisposable?)_nav)?.Dispose();
                }
                else
                {
                    _nav?.ClearCache("MainRegion");
                }
            });
        }

        public void EnableReentrantDispose(INavigationService nav)
        {
            _nav = nav;
            _reenterDispose = true;
        }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task Navigate_RequestRegisteredDuringGuard_SupersedesDepartureCandidate()
    {
        // A request registered while the leave guard is still running does not
        // execute concurrently: it waits in the window's slot, is sampled when
        // the window closes, and supersedes the request that started the
        // departure — which therefore never constructs its page.
        ConcurrentTargetViewModel.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ConcurrentGuardViewModel>();
        services.AddTransient<ConcurrentTargetViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ConcurrentGuardViewModel>();
        var guardVm = Assert.IsType<ConcurrentGuardViewModel>(nav.GetCurrentViewModel());

        // First navigation starts a departure and blocks in the guard.
        var firstNav = Task.Run(() => nav.NavigateToAsync<ConcurrentTargetViewModel>());
        // Wait until the guard is actually running.
        await guardVm.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Second request registers into the slot while the guard is running.
        // It cannot complete before the window closes.
        var secondNav = nav.NavigateToAsync<TestDetailViewModel>();
        Assert.False(secondNav.IsCompleted);

        // Release the guard: the departure finishes, the slot is sampled, and
        // the second request wins.
        guardVm.ReleaseGuard();
        var firstResult = await firstNav.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(firstResult);
        var secondResult = await secondNav.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(secondResult);
        // The superseded request never constructed its page: nothing to dispose.
        Assert.Equal(0, ConcurrentTargetViewModel.DisposeCount);
        // Active page is the second request's target.
        Assert.IsType<TestDetailViewModel>(nav.GetCurrentViewModel());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // For effects that land just after an intent's fate completes (the
        // fate is decided at the sampling point; the winning hop or the
        // revive runs immediately afterwards on the pump).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within 10 seconds.");
            }

            await Task.Delay(10);
        }
    }

    private TaskCompletionSource RestSignalFor<TViewModel>()
    {
        // RegionNavigated fires exactly when a page comes to rest as the
        // region's settled current page, so it is a deterministic settle probe.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.RegionNavigated += (_, vm) =>
        {
            if (vm is TViewModel)
            {
                tcs.TrySetResult();
            }
        };
        return tcs;
    }

    [Fact]
    public async Task Window_ToRedirect_IntermediateRunsFullCycle_AndOnlyLandingPageRaisesRegionNavigated()
    {
        ToRedirectViewModel.Reset();
        ChainLandingViewModel.Reset();
        var navigatedTypes = new List<Type>();
        _navigationService.RegionNavigated += (_, vm) =>
        {
            lock (navigatedTypes) navigatedTypes.Add(vm!.GetType());
        };
        var rest = RestSignalFor<ChainLandingViewModel>();

        var result = await _navigationService.NavigateToAsync<ToRedirectViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        await rest.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The intermediate page ran a complete, correctly ordered cycle.
        Assert.Equal(1, ToRedirectViewModel.ToCount);
        Assert.Equal(1, ToRedirectViewModel.FromCount);
        Assert.Equal(1, ChainLandingViewModel.ConstructionCount);
        Assert.IsType<ChainLandingViewModel>(_navigationService.GetCurrentViewModel());
        // Only the page that came to rest raised RegionNavigated.
        Assert.Equal(new[] { typeof(ChainLandingViewModel) }, navigatedTypes);
    }

    [Fact]
    public async Task Window_FromRedirect_SupersedesButtonRequest_AndButtonTargetNeverConstructed()
    {
        FromRedirectViewModel.Reset();
        ButtonTargetViewModel.Reset();
        await _navigationService.NavigateToAsync<FromRedirectViewModel>();
        var rest = RestSignalFor<FromLandingViewModel>();

        var buttonNav = _navigationService.NavigateToAsync<ButtonTargetViewModel>();
        var buttonResult = await buttonNav.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(buttonResult);
        await rest.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, FromRedirectViewModel.FromCount);
        Assert.Equal(0, ButtonTargetViewModel.ConstructionCount);
        Assert.IsType<FromLandingViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_AsyncFrom_SecondRegistrationDuringFrom_Wins()
    {
        ButtonTargetViewModel.Reset();
        await _navigationService.NavigateToAsync<SlowFromViewModel>();
        var slow = Assert.IsType<SlowFromViewModel>(_navigationService.GetCurrentViewModel());

        var firstNav = _navigationService.NavigateToAsync<ButtonTargetViewModel>();
        await slow.FromStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Registered while the first request's From is still running: it is
        // sampled when the window closes and wins.
        var secondNav = _navigationService.NavigateToAsync<FromLandingViewModel>();
        slow.ReleaseFrom.TrySetResult();

        Assert.False(await firstNav.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await secondNav.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, ButtonTargetViewModel.ConstructionCount);
        Assert.IsType<FromLandingViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_ChainOfRedirects_ConstructsEveryHop_AndRaisesRegionNavigatedOnce()
    {
        ChainFirstViewModel.Reset();
        ChainSecondViewModel.Reset();
        ChainThirdViewModel.Reset();
        var navigatedTypes = new List<Type>();
        _navigationService.RegionNavigated += (_, vm) =>
        {
            lock (navigatedTypes) navigatedTypes.Add(vm!.GetType());
        };
        var rest = RestSignalFor<ChainThirdViewModel>();

        var result = await _navigationService.NavigateToAsync<ChainFirstViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        await rest.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Every executed hop was constructed and ran a complete cycle.
        Assert.Equal(1, ChainFirstViewModel.ToCount);
        Assert.Equal(1, ChainFirstViewModel.FromCount);
        Assert.Equal(1, ChainSecondViewModel.ToCount);
        Assert.Equal(1, ChainSecondViewModel.FromCount);
        Assert.Equal(1, ChainThirdViewModel.ToCount);
        Assert.IsType<ChainThirdViewModel>(_navigationService.GetCurrentViewModel());
        Assert.Equal(new[] { typeof(ChainThirdViewModel) }, navigatedTypes);
    }

    [Fact]
    public async Task Window_RedirectLoop_StopsAtChainLimit_ReportsError_AndServiceStaysUsable()
    {
        LoopAViewModel.Reset();
        LoopBViewModel.Reset();
        var loopError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.SubscriberError += ex =>
        {
            if (ex is InvalidOperationException && ex.Message.Contains("Navigation chain exceeded"))
            {
                loopError.TrySetResult(ex);
            }
        };

        var result = await _navigationService.NavigateToAsync<LoopAViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);

        await loopError.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The loop stopped: construction counts are bounded by the chain
        // limit (plus the initial landing) and no longer grow.
        var constructions = LoopAViewModel.ConstructionCount + LoopBViewModel.ConstructionCount;
        Assert.InRange(constructions, 2, 12);
        await Task.Delay(300);
        Assert.Equal(constructions,
            LoopAViewModel.ConstructionCount + LoopBViewModel.ConstructionCount);

        // The page whose departure was refused is still the intact current page.
        Assert.True(_navigationService.GetCurrentViewModel() is LoopAViewModel or LoopBViewModel);

        // The service remains usable.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Window_GuardVeto_DrainsRegisteredIntents_AndPageStaysIntact()
    {
        ButtonTargetViewModel.Reset();
        await _navigationService.NavigateToAsync<VetoGuardViewModel>();
        var guard = Assert.IsType<VetoGuardViewModel>(_navigationService.GetCurrentViewModel());
        guard.Veto = true;

        // The guard registers its own request and then vetoes: the departure
        // does not start, both requests are drained unexecuted.
        var result = await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result);
        Assert.Same(guard, _navigationService.GetCurrentViewModel());
        Assert.Equal(0, guard.NavigatedFromCount);
        Assert.Equal(0, ButtonTargetViewModel.ConstructionCount);

        guard.Veto = false;
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Window_ResolutionFailure_FaultsRequest_AndRevivesCurrentPage()
    {
        await _navigationService.NavigateToAsync<CountingAwareViewModel>();
        var home = Assert.IsType<CountingAwareViewModel>(_navigationService.GetCurrentViewModel());
        Assert.Equal(1, home.NavigatedToCount);

        // UnregisteredViewModel is deliberately not in the container: its
        // resolution fails after the current page already ran From.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _navigationService.NavigateToAsync<UnregisteredViewModel>()
                .WaitAsync(TimeSpan.FromSeconds(10)));

        // The current page was revived: To ran again with its original
        // parameter, restoring the To/From pairing, and it is still current.
        await WaitUntilAsync(() => home.NavigatedToCount == 2);
        Assert.Same(home, _navigationService.GetCurrentViewModel());
        Assert.Equal(1, home.NavigatedFromCount);
    }

    [Fact]
    public async Task Window_SelfRefreshRegisteredInFrom_RevivesWithNewParameter()
    {
        ButtonTargetViewModel.Reset();
        await _navigationService.NavigateToAsync<SelfRefreshFromViewModel, string>("one");
        var page = Assert.IsType<SelfRefreshFromViewModel>(_navigationService.GetCurrentViewModel());
        Assert.Equal("one", page.LastParameter);

        // The revive re-activates the page as the region's resting current
        // page, so RegionNavigated must be raised for it again (as a refresh
        // would), with the same instance.
        object? revivedVm = null;
        var reviveRaised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.RegionNavigated += (_, vm) =>
        {
            if (vm is SelfRefreshFromViewModel)
            {
                revivedVm = vm;
                reviveRaised.TrySetResult();
            }
        };

        // The departure's From registers a refresh back to the page itself:
        // the sampled intent targets the departing page, so the button request
        // is superseded and the page is revived in place with the new parameter.
        var buttonNav = _navigationService.NavigateToAsync<ButtonTargetViewModel>();
        Assert.False(await buttonNav.WaitAsync(TimeSpan.FromSeconds(10)));

        await reviveRaised.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(page, revivedVm);
        await WaitUntilAsync(() => page.NavigatedToCount == 2);
        Assert.Same(page, _navigationService.GetCurrentViewModel());
        Assert.Equal(0, ButtonTargetViewModel.ConstructionCount);
        Assert.Equal(1, page.NavigatedFromCount);
        Assert.Equal("two", page.LastParameter);
    }

    [Fact]
    public async Task Window_GoBack_SupersededByFromRegisteredNavigate()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<FromRedirectViewModel>();
        var rest = RestSignalFor<FromLandingViewModel>();

        var backNav = _navigationService.GoBackAsync();
        Assert.False(await backNav.WaitAsync(TimeSpan.FromSeconds(10)));
        await rest.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The From-registered navigation was pushed instead of going back.
        Assert.IsType<FromLandingViewModel>(_navigationService.GetCurrentViewModel());
        Assert.True(await _navigationService.GoBackAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<FromRedirectViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_Navigate_SupersededByFromRegisteredGoBack()
    {
        ButtonTargetViewModel.Reset();
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<FromGoBackViewModel>();
        var rest = RestSignalFor<TestHomeViewModel>();

        var nav = _navigationService.NavigateToAsync<ButtonTargetViewModel>();
        Assert.False(await nav.WaitAsync(TimeSpan.FromSeconds(10)));
        await rest.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, ButtonTargetViewModel.ConstructionCount);
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task PopToExisting_DerivedRegistration_StillResolvesAndReactivates()
    {
        ThrowawayCountingViewModel.Reset();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ThrowawayCountingViewModel, DerivedThrowawayCountingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<ThrowawayCountingViewModel>(
            retention: NavigationRetention.KeepAlive));
        var first = nav.GetCurrentViewModel();
        Assert.IsType<DerivedThrowawayCountingViewModel>(first);
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>()); // cover it

        // The pre-check keys on the exact runtime type, which only
        // resolution can know for a derived registration: the navigation
        // still resolves (and discards) an instance, and still reactivates
        // the covered entry.
        Assert.True(await nav.NavigateToAsync<ThrowawayCountingViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.Equal(2, ThrowawayCountingViewModel.ConstructionCount);
        Assert.Same(first, nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task PopToExisting_MixedRegistrations_ExactTypeEntryFromOtherServiceTypeIsNotReused()
    {
        // The base type's own registration resolves to the derived type,
        // while a second service type resolves to the base type exactly.
        // The covered KeepAlive entry (exact base runtime type, created by
        // the interface navigation) must NOT satisfy a navigation to the
        // base service type: that navigation resolves the derived type,
        // whose stack scan matches neither the entry's instance nor type.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ThrowawayCountingViewModel, DerivedThrowawayCountingViewModel>();
        services.AddTransient<IThrowawayCountingTarget, ThrowawayCountingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<IThrowawayCountingTarget>(
            retention: NavigationRetention.KeepAlive));
        var viaInterface = nav.GetCurrentViewModel();
        Assert.IsType<ThrowawayCountingViewModel>(viaInterface);
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>()); // cover it

        Assert.True(await nav.NavigateToAsync<ThrowawayCountingViewModel>());
        Assert.IsType<DerivedThrowawayCountingViewModel>(nav.GetCurrentViewModel());
        Assert.NotSame(viaInterface, nav.GetCurrentViewModel());
    }

    [Fact]
    public async Task PopToExisting_MixedRegistrations_SingletonEntryIsNotPoppedByInterfaceEntry()
    {
        // The base service type is a singleton resolving to the derived
        // type; the interface service type resolves to the exact base.
        // Re-navigating to the base type must reactivate the singleton's
        // own entry by instance — not pop past it to the covered
        // interface entry (which would also dispose the singleton's
        // stack entry).
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<ThrowawayCountingViewModel, DerivedThrowawayCountingViewModel>();
        services.AddTransient<IThrowawayCountingTarget, ThrowawayCountingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<IThrowawayCountingTarget>(
            retention: NavigationRetention.KeepAlive));
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>()); // cover it
        Assert.True(await nav.NavigateToAsync<ThrowawayCountingViewModel>());
        var singleton = nav.GetCurrentViewModel();
        Assert.IsType<DerivedThrowawayCountingViewModel>(singleton);

        Assert.True(await nav.NavigateToAsync<ThrowawayCountingViewModel>());
        Assert.Same(singleton, nav.GetCurrentViewModel());
        Assert.True(nav.CanGoBack()); // the stack was not popped past the singleton
    }

    [Fact]
    public async Task GetNavigationStack_ReturnsBottomToTopSnapshot()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>());
        var home = _navigationService.GetNavigationStack()[0];
        var top = _navigationService.GetCurrentViewModel();

        var snapshot = _navigationService.GetNavigationStack();

        Assert.Equal(3, snapshot.Count);
        Assert.IsType<TestHomeViewModel>(snapshot[0]);
        Assert.IsType<TestDetailViewModel>(snapshot[1]);
        Assert.IsType<KeepAliveViewModel>(snapshot[2]);
        Assert.Same(snapshot[0], home);
        Assert.Same(snapshot[2], top);
    }

    [Fact]
    public async Task GetNavigationStack_SnapshotIsIsolatedFromLaterNavigations()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        var snapshot = _navigationService.GetNavigationStack();

        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>());
        Assert.True(await _navigationService.GoBackAsync());

        Assert.Single(snapshot);
        Assert.IsType<TestHomeViewModel>(snapshot[0]);
        Assert.Single(_navigationService.GetNavigationStack());
    }

    [Fact]
    public async Task GetNavigationStack_EmptyForUnknownRegion_AndExcludesCachedPages()
    {
        Assert.Empty(_navigationService.GetNavigationStack("NoSuchRegion"));

        // A page popped into the KeepAlive cache is no longer on the
        // stack and must not appear in the snapshot.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.True(await _navigationService.GoBackAsync());
        var snapshot = _navigationService.GetNavigationStack();
        Assert.Single(snapshot);
        Assert.IsType<TestHomeViewModel>(snapshot[0]);
    }

    [Fact]
    public async Task GetNavigationStack_PaddedRegionName_AddressesSameStack()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>("  MainRegion  "));
        Assert.Single(_navigationService.GetNavigationStack(" MainRegion "));
        Assert.Single(_navigationService.GetNavigationStack("MainRegion"));
    }

    [Fact]
    public async Task GetCurrentParameter_ReturnsTheInstancesMostRecentActivationParameter()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        var home = _navigationService.GetCurrentViewModel()!;
        // A page activated without a parameter reports null.
        Assert.Null(_navigationService.GetCurrentParameter(home));

        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first"));
        var detail = _navigationService.GetCurrentViewModel()!;
        Assert.Equal("first", _navigationService.GetCurrentParameter(detail));
        // The covered page keeps its own parameter; the lookup is keyed by
        // the instance, not by whichever page is on top.
        Assert.Null(_navigationService.GetCurrentParameter(home));
        Assert.Null(_navigationService.GetCurrentParameter(detail, "NoSuchRegion"));
        Assert.Null(_navigationService.GetCurrentParameter(new TestHomeViewModel()));
        Assert.Throws<ArgumentNullException>(() => _navigationService.GetCurrentParameter(null!));

        // A refresh re-activates the page with the new parameter.
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>(
            "second", refreshIfActive: true));
        Assert.Equal("second", _navigationService.GetCurrentParameter(detail));

        // Once the instance has left the stack it is no longer found.
        Assert.True(await _navigationService.GoBackAsync());
        Assert.Null(_navigationService.GetCurrentParameter(detail));
    }

    [Fact]
    public async Task GetCurrentParameter_AfterDispose_ReturnsNull()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("x"));
        var detail = _navigationService.GetCurrentViewModel()!;
        Assert.Equal("x", _navigationService.GetCurrentParameter(detail));

        await ((IAsyncDisposable)_navigationService).DisposeAsync();

        Assert.Null(_navigationService.GetCurrentParameter(detail));
    }

    // ───────────────────── GoBackTo / SetRoot / Remove ─────────────────────

    [Fact]
    public async Task GoBackTo_PopsIntermediatePages_AndReactivatesTarget()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        var home = _navigationService.GetCurrentViewModel();
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first"));
        var detail = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        Assert.True(await _navigationService.GoBackToAsync<TestHomeViewModel>());

        Assert.Same(home, _navigationService.GetCurrentViewModel());
        Assert.True(detail.Disposed);
        Assert.Single(_navigationService.GetNavigationStack());
    }

    [Fact]
    public async Task GoBackTo_TargetNotOnStack_ReturnsFalseAndKeepsStack()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first"));
        var detail = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.False(await _navigationService.GoBackToAsync<SimpleAwareViewModel>());

        Assert.Same(detail, _navigationService.GetCurrentViewModel());
        Assert.False(detail.Disposed);
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);
    }

    [Fact]
    public async Task GoBackTo_TargetIsActivePage_ReturnsFalse()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first"));
        var detail = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.False(await _navigationService.GoBackToAsync<TestDetailViewModel>());

        Assert.Same(detail, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);
    }

    [Fact]
    public async Task GoBackTo_Parameterized_SelectsTheClaimedInstance()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("a"));
        var detailA = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("b"));
        var detailB = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        // An unclaimed parameter goes nowhere.
        Assert.False(await _navigationService.GoBackToAsync<TestDetailViewModel, string>("nope"));
        Assert.Equal(5, _navigationService.GetNavigationStack().Count);

        Assert.True(await _navigationService.GoBackToAsync<TestDetailViewModel, string>("a"));

        Assert.Same(detailA, _navigationService.GetCurrentViewModel());
        Assert.Equal("a", detailA.ReceivedParam);
        Assert.True(detailB.Disposed);
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);
    }

    [Fact]
    public async Task GoBackTo_GuardRunsOnce_WithTargetContext()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first"));
        // An intermediate page sits between the target and the departing
        // page, so the context cannot be confused with the page directly
        // below the departing one.
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ContextGuardViewModel>());
        var guard = (ContextGuardViewModel)_navigationService.GetCurrentViewModel()!;

        Assert.True(await _navigationService.GoBackToAsync<TestDetailViewModel>());

        // The departing page's guard ran exactly once, and its context
        // describes the target page: its type and its stored parameter.
        Assert.Equal(1, guard.CallCount);
        Assert.NotNull(guard.LastContext);
        Assert.True(guard.LastContext!.IsBack);
        Assert.Equal(typeof(TestDetailViewModel), guard.LastContext.TargetViewModelType);
        Assert.Equal("first", guard.LastContext.Parameter);
        Assert.Null(guard.LastContext.Operation);
        Assert.Null(guard.LastContext.Retention);
    }

    [Fact]
    public async Task GoBackTo_VetoedByGuard_ReturnsFalse()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<VetoGuardViewModel>());
        var veto = (VetoGuardViewModel)_navigationService.GetCurrentViewModel()!;
        veto.Veto = true;

        Assert.False(await _navigationService.GoBackToAsync<TestHomeViewModel>());

        Assert.Same(veto, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);
    }

    [Fact]
    public async Task GoBackTo_KeepAliveIntermediate_MovesToCacheNotDisposed()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive));
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        Assert.True(await _navigationService.GoBackToAsync<TestHomeViewModel>());

        Assert.False(keepAlive.Disposed);
        // The instance waits in the cache and is adopted on the next navigation.
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive));
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task SetRoot_ReplacesWholeStack_WithFreshInstance()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        var home = _navigationService.GetCurrentViewModel();
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("first"));
        var detail = (TestDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        Assert.True(await _navigationService.SetRootAsync<TestHomeViewModel>());

        var stack = _navigationService.GetNavigationStack();
        Assert.Single(stack);
        Assert.IsType<TestHomeViewModel>(stack[0]);
        Assert.NotSame(home, stack[0]);
        Assert.True(detail.Disposed);
    }

    [Fact]
    public async Task SetRoot_Parameterized_AdoptsRecognizedCacheEntry()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "p", retention: NavigationRetention.KeepAlive));
        var cached = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        // Pop it into the cache, then cover the stack with other pages.
        Assert.True(await _navigationService.GoBackAsync());
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        Assert.True(await _navigationService.SetRootAsync<ParamAwareKeepAliveViewModel, string>(
            "p", retention: NavigationRetention.KeepAlive));

        Assert.Same(cached, _navigationService.GetCurrentViewModel());
        Assert.Single(_navigationService.GetNavigationStack());
        Assert.False(cached.Disposed);
    }

    [Fact]
    public async Task SetRoot_VetoedByGuard_ReturnsFalse()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<VetoGuardViewModel>());
        var veto = (VetoGuardViewModel)_navigationService.GetCurrentViewModel()!;
        veto.Veto = true;

        Assert.False(await _navigationService.SetRootAsync<SimpleAwareViewModel>());

        Assert.Same(veto, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);
    }

    [Fact]
    public async Task Remove_CoveredPagesOnly_ActivePageKept()
    {
        TrackedDisposeViewModel.DisposeCount = 0;
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TrackedDisposeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        // The covered Destroy page is removed and its scope disposed.
        Assert.Equal(1, _navigationService.Remove<TrackedDisposeViewModel>());
        Assert.Equal(1, TrackedDisposeViewModel.DisposeCount);
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);

        // The active page is never removed, even when it matches.
        Assert.Equal(0, _navigationService.Remove<SimpleAwareViewModel>());
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);

        // The covered bottom page can be removed too.
        Assert.Equal(1, _navigationService.Remove<TestHomeViewModel>());
        Assert.Single(_navigationService.GetNavigationStack());
    }

    [Fact]
    public async Task Remove_NoMatch_ReturnsZero()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());

        Assert.Equal(0, _navigationService.Remove<TestDetailViewModel>());
        Assert.Single(_navigationService.GetNavigationStack());
    }

    [Fact]
    public async Task Remove_KeepAlivePage_MovesToCache()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive));
        var keepAlive = (KeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        Assert.Equal(1, _navigationService.Remove<KeepAliveViewModel>());

        Assert.False(keepAlive.Disposed);
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(retention: NavigationRetention.KeepAlive));
        Assert.Same(keepAlive, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Remove_Parameterized_RemovesClaimedOnly()
    {
        // ParamAwareKeepAliveViewModel is synchronously disposable: Remove is
        // a synchronous API and cannot tear down IAsyncDisposable-only pages
        // (the same documented limit as ClearCache).
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>("a"));
        var pageA = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        // Cover page A before pushing page B: pushing the same type while it
        // is active is a self no-op, not a second instance.
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>("b"));
        var pageB = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(pageA, pageB);
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        Assert.Equal(1, _navigationService.Remove<ParamAwareKeepAliveViewModel, string>("a"));
        Assert.True(pageA.Disposed);
        Assert.False(pageB.Disposed);
        Assert.Equal(4, _navigationService.GetNavigationStack().Count);

        Assert.Equal(1, _navigationService.Remove<ParamAwareKeepAliveViewModel, string>("b"));
        Assert.True(pageB.Disposed);
        Assert.Equal(3, _navigationService.GetNavigationStack().Count);
    }

    [Fact]
    public async Task Remove_KeepAlivePage_EvictsCachedInstance_AndDisposesIt()
    {
        // First instance ends up in the cache (stored parameter "a").
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "a", retention: NavigationRetention.KeepAlive));
        var first = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.GoBackAsync());

        // A second instance: the cached one is not claimed for "b", so it is
        // bypassed and a fresh KeepAlive page is pushed.
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "b", retention: NavigationRetention.KeepAlive));
        var second = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        // Removing the covered KeepAlive page moves it into the cache slot:
        // the previously cached instance is evicted and disposed.
        Assert.Equal(1, _navigationService.Remove<ParamAwareKeepAliveViewModel>());
        Assert.True(first.Disposed);
        Assert.False(second.Disposed);

        // The cache now holds the second instance.
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "b", retention: NavigationRetention.KeepAlive));
        Assert.Same(second, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Remove_TwoKeepAlivePagesOfOneKind_DeepestWinsCacheSlot()
    {
        // Two covered KeepAlive pages of one kind on the stack.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "a", retention: NavigationRetention.KeepAlive));
        var pageA = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "b", retention: NavigationRetention.KeepAlive));
        var pageB = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(pageA, pageB);
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        // One call removes both. They are deactivated top first: page B
        // moves into the cache slot first, then page A moves in and evicts
        // it — the deepest removed page wins the slot, the other is
        // disposed.
        Assert.Equal(2, _navigationService.Remove<ParamAwareKeepAliveViewModel>());
        Assert.False(pageA.Disposed);
        Assert.True(pageB.Disposed);
        Assert.Equal(3, _navigationService.GetNavigationStack().Count);

        // The cache holds page A.
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "a", retention: NavigationRetention.KeepAlive));
        Assert.Same(pageA, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Remove_Parameterized_TwoClaimedKeepAlivePages_DeepestWinsCacheSlot()
    {
        // Two covered KeepAlive pages of one kind claimed by one parameter.
        // The second instance is created with Replace, which performs no
        // stack recognition — a Push for "a" would be claimed by page A.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "a", retention: NavigationRetention.KeepAlive));
        var pageA = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "a", operation: NavigationOperation.Replace, retention: NavigationRetention.KeepAlive));
        var pageB = (ParamAwareKeepAliveViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(pageA, pageB);
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        // One parameterized call claims and removes both; deactivation is
        // top first, so page A — processed last — wins the cache slot and
        // page B is evicted and disposed.
        Assert.Equal(2, _navigationService.Remove<ParamAwareKeepAliveViewModel, string>("a"));
        Assert.False(pageA.Disposed);
        Assert.True(pageB.Disposed);
        Assert.Equal(2, _navigationService.GetNavigationStack().Count);

        // The cache holds page A.
        Assert.True(await _navigationService.NavigateToAsync<ParamAwareKeepAliveViewModel, string>(
            "a", retention: NavigationRetention.KeepAlive));
        Assert.Same(pageA, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task GoBackTo_RegisteredInCallback_ExecutesAfterWindowCloses()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        var home = _navigationService.GetCurrentViewModel();
        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel, string>("x"));
        Assert.True(await _navigationService.NavigateToAsync<FromGoBackToViewModel>());

        // Leaving the redirect page runs its From, whose registered
        // GoBackTo supersedes the GoBack that started the departure: the
        // outer request completes as superseded (false) while the
        // registered GoBackTo executes and lands on Home.
        var rest = RestSignalFor<TestHomeViewModel>();
        Assert.False(await _navigationService.GoBackAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await rest.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(home, _navigationService.GetCurrentViewModel());
        Assert.Single(_navigationService.GetNavigationStack());
    }

    [Fact]
    public async Task GoBackTo_ThrowingVote_ReportedExactlyOnce_WhenGuardVetoes()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += errors.Add;

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ConditionalThrowVoteViewModel, string>("buddy"));
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<ConditionalThrowVoteViewModel, string>("thrower"));
        Assert.True(await _navigationService.NavigateToAsync<VetoGuardViewModel>());
        var veto = (VetoGuardViewModel)_navigationService.GetCurrentViewModel()!;
        veto.Veto = true;

        // The search examines the "thrower" instance first (its vote throws,
        // collected), then claims the "buddy" instance — the departure
        // proceeds, but the guard vetoes it, so the hop never runs. The
        // vote failure must still be reported, exactly once.
        Assert.False(await _navigationService.GoBackToAsync<ConditionalThrowVoteViewModel, string>("seek"));

        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("vote failed", error.Message);
        Assert.Same(veto, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task GoBackTo_VoteClaimed_OverwritesStoredParameter()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<VoteClaimViewModel, string>("stored"));
        var vote = (VoteClaimViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.True(await _navigationService.NavigateToAsync<SimpleAwareViewModel>());

        // The vote claims the page for "claimed", a parameter different from
        // the stored one: the page is re-activated with the intent's
        // parameter, which becomes its stored parameter.
        Assert.True(await _navigationService.GoBackToAsync<VoteClaimViewModel, string>("claimed"));

        Assert.Same(vote, _navigationService.GetCurrentViewModel());
        Assert.Equal("claimed", vote.LastParameter);
        Assert.Equal("claimed", _navigationService.GetCurrentParameter(vote));
    }

    [Fact]
    public async Task Remove_AfterDispose_ReturnsZero()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<TrackedDisposeViewModel>());

        await ((IAsyncDisposable)_navigationService).DisposeAsync();

        Assert.Equal(0, _navigationService.Remove<TestHomeViewModel>());
    }

    [Fact]
    public async Task Remove_FromLifecycleCallback_ThrowsInvalidOperationException()
    {
        RemoveInCallbackViewModel.Caught = null;
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await _navigationService.NavigateToAsync<RemoveInCallbackViewModel>());

        Assert.IsType<InvalidOperationException>(RemoveInCallbackViewModel.Caught);
    }

    [Fact]
    public async Task RegionName_SurroundingWhitespace_AddressesSameRegionAcrossApis()
    {
        // Names are trimmed at the API boundary: every spelling of the
        // same trimmed name addresses the same region.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>("  PaddedRegion  ")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel("PaddedRegion"));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel(" PaddedRegion "));

        Assert.True(await _navigationService.NavigateToAsync<TestDetailViewModel>("PaddedRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(_navigationService.CanGoBack("\tPaddedRegion "));
        Assert.True(await _navigationService.GoBackAsync(" PaddedRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel("PaddedRegion"));
    }

    [Fact]
    public async Task RegionName_PaddedClearCache_ClearsTheTrimmedRegionsCache()
    {
        string? clearedRegion = null;
        using var subscription = _navigationService.SubscribeRegionCacheCleared(r => clearedRegion = r);

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>("CacheRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(
            "CacheRegion", retention: NavigationRetention.KeepAlive).WaitAsync(TimeSpan.FromSeconds(10)));
        var first = _navigationService.GetCurrentViewModel("CacheRegion");

        // Pop the KeepAlive page into the cache, then clear it through a
        // padded spelling: the trimmed region's cache is cleared and the
        // event reports the normalized name.
        Assert.True(await _navigationService.GoBackAsync("CacheRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        _navigationService.ClearCache("  CacheRegion  ");
        Assert.Equal("CacheRegion", clearedRegion);

        // The cached instance is gone: navigating there again creates a
        // new one instead of reusing the KeepAlive entry.
        Assert.True(await _navigationService.NavigateToAsync<KeepAliveViewModel>(
            "CacheRegion", retention: NavigationRetention.KeepAlive).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotSame(first, _navigationService.GetCurrentViewModel("CacheRegion"));
    }

    [Fact]
    public async Task RegionName_ComparisonIsOrdinalCaseSensitive()
    {
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>("CaseRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel("CaseRegion"));
        Assert.Null(_navigationService.GetCurrentViewModel("caseregion"));
        Assert.Null(_navigationService.GetCurrentViewModel("CASEREGION"));
    }

    [Fact]
    public async Task RegionName_WhitespaceOnly_StillRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _navigationService.NavigateToAsync<TestHomeViewModel>("   "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _navigationService.GoBackAsync(" "));
        Assert.Throws<ArgumentException>(() => _navigationService.GetCurrentViewModel("\t"));
        Assert.Throws<ArgumentException>(() => _navigationService.ClearCache("  "));
    }

    [Fact]
    public async Task Window_GoBackOnUnusedRegion_WindowIsReclaimed()
    {
        var nav = (NavigationService)_navigationService;

        // A GoBack against a region that was never used creates a window
        // for the request; once the request settles (false — empty stack),
        // the idle window is reclaimed instead of accumulating per name.
        Assert.False(await _navigationService.GoBackAsync("DynUnusedRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => !WindowsContain(nav, "DynUnusedRegion"));
    }

    [Fact]
    public async Task Window_ProbedRegion_WindowIsReclaimed_AndRegionReusable()
    {
        var nav = (NavigationService)_navigationService;

        // Probe a fresh region name (a GoBack that can only fail), let the
        // window be reclaimed, then use the name for real: the recreated
        // window must execute the hop normally.
        Assert.False(await _navigationService.GoBackAsync("DynProbeRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => !WindowsContain(nav, "DynProbeRegion"));

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>("DynProbeRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel("DynProbeRegion"));
        Assert.True(WindowsContain(nav, "DynProbeRegion"));
    }

    [Fact]
    public async Task Window_RestingRegionWithPage_WindowIsKept()
    {
        var nav = (NavigationService)_navigationService;

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>("DynRestingRegion")
            .WaitAsync(TimeSpan.FromSeconds(10)));

        // Give the pump its chance to settle: with a live page on the
        // stack the window must NOT be reclaimed — its surfacing state
        // belongs to the resting page.
        await Task.Delay(300);
        Assert.True(WindowsContain(nav, "DynRestingRegion"));
    }

    [Fact]
    public async Task Window_WithdrawnRegistrationDuringSlowTo_DoesNotCloseWindowEarly()
    {
        var navigated = new List<object>();
        _navigationService.RegionNavigated += (_, vm) =>
        {
            lock (navigated) navigated.Add(vm!);
        };

        var navTask = _navigationService.NavigateToAsync<SlowToViewModel>();
        // Reach the page instance through a completed short navigation probe:
        // the instance exists once its scope was created; poll for it.
        SlowToViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowToViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Register a follow-up request and withdraw it immediately (the caller
        // cancels before the entering page's To has finished). The wake this
        // causes is void: the window must stay open until the To completes.
        using var cts = new CancellationTokenSource();
        var withdrawn = _navigationService.NavigateToAsync<ChainLandingViewModel>(cancellationToken: cts.Token);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await withdrawn);

        await Task.Delay(500);
        Assert.False(navTask.IsCompleted);
        lock (navigated) Assert.Empty(navigated);

        // Let the activation finish: only now may the page rest and surface.
        page.ReleaseTo.TrySetResult();
        Assert.True(await navTask.WaitAsync(TimeSpan.FromSeconds(10)));
        lock (navigated) Assert.Single(navigated);
        Assert.Same(page, _navigationService.GetCurrentViewModel());

        // Navigating away afterwards runs From strictly after the To completed.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(page.FromRanBeforeToCompleted);
    }

    [Fact]
    public async Task Window_ClaimedRegistrationWithdrawnAfterClaim_RestsAndSurfacesBeforeFateCompletes()
    {
        var navigated = new List<object>();
        _navigationService.RegionNavigated += (_, vm) =>
        {
            lock (navigated) navigated.Add(vm!);
        };

        var navTask = _navigationService.NavigateToAsync<SlowClaimViewModel>();
        SlowClaimViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowClaimViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Register a follow-up request and do NOT withdraw it yet: the
        // entering step judges the wake while the request is still live
        // and claims it — the claim is observable as the entering page's
        // activation token being cancelled.
        using var cts = new CancellationTokenSource();
        var claimedTask = _navigationService.NavigateToAsync<ChainLandingViewModel>(cancellationToken: cts.Token);
        await WaitUntilAsync(() => page.ToToken.IsCancellationRequested);

        // Withdraw AFTER the claim: too late to void the wake at judgment
        // time, but the caller's fate still cancels.
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await claimedTask);

        // Let the activation finish. The claimed candidate is dead, so no
        // departure may start — a departure would invoke the page's guard
        // (which parks on its gate once invoked), and its From after that.
        // The page comes to rest, and its RegionNavigated must already
        // have been delivered at the moment the entered fate completes —
        // the same order the unclaimed rest keeps. (Regression: the
        // claimed path used to complete the entered fate first, then
        // start the dead departure anyway — guard included — and surface
        // the page only afterwards, from the settle step.)
        page.ReleaseTo.TrySetResult();
        try
        {
            Assert.True(await navTask.WaitAsync(TimeSpan.FromSeconds(10)));

            var guardWait = await Task.WhenAny(page.GuardStarted.Task, Task.Delay(500));
            Assert.NotSame(page.GuardStarted.Task, guardWait);
            Assert.False(page.FromStarted.Task.IsCompleted);
            lock (navigated) Assert.Single(navigated);
            Assert.Same(page, _navigationService.GetCurrentViewModel());

            // The claim cancelled the page's lifecycle source to close
            // its window; resting renews it, like every other
            // page-stays exit in the departure core.
            var nav = (NavigationService)_navigationService;
            var entry = GetNavigationEntry(nav, "MainRegion", typeof(SlowClaimViewModel));
            Assert.NotNull(entry);
            var lifecycleCts = (CancellationTokenSource)entry.GetType()
                .GetProperty("LifecycleCts",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(entry)!;
            Assert.False(lifecycleCts.IsCancellationRequested);
        }
        finally
        {
            page.ReleaseGuard.TrySetResult();
            page.ReleaseFrom.TrySetResult();
        }
    }

    [Fact]
    public async Task Window_ClaimedWithdrawnWithLiveIntentPending_DoesNotSurfaceEnteringPage()
    {
        var navigated = new List<object>();
        _navigationService.RegionNavigated += (_, vm) =>
        {
            lock (navigated) navigated.Add(vm!);
        };

        var navTask = _navigationService.NavigateToAsync<SlowClaimViewModel>();
        SlowClaimViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowClaimViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var claimedTask = _navigationService.NavigateToAsync<ChainLandingViewModel>(cancellationToken: cts.Token);
        await WaitUntilAsync(() => page.ToToken.IsCancellationRequested);

        // A further live request lands in the slot while the claimed
        // candidate is still the window's departure candidate.
        var nextTask = _navigationService.NavigateToAsync<TestHomeViewModel>();

        // Withdraw the claimed candidate and let the activation finish:
        // the entering page is already leaving for the live request, so
        // it must not be surfaced as a resting page on its way out —
        // only the live request's landing may be reported.
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await claimedTask);
        page.ReleaseTo.TrySetResult();
        try
        {
            Assert.True(await navTask.WaitAsync(TimeSpan.FromSeconds(10)));
            await page.GuardStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            page.ReleaseGuard.TrySetResult();
            await page.FromStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            page.ReleaseFrom.TrySetResult();
            Assert.True(await nextTask.WaitAsync(TimeSpan.FromSeconds(10)));

            lock (navigated)
            {
                var only = Assert.Single(navigated);
                Assert.IsType<TestHomeViewModel>(only);
            }

            Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        }
        finally
        {
            page.ReleaseGuard.TrySetResult();
            page.ReleaseFrom.TrySetResult();
        }
    }

    [Fact]
    public async Task Window_SelfNavigationDuringSlowTo_DoesNotCancelActivationToken()
    {
        var navigated = new List<object>();
        _navigationService.RegionNavigated += (_, vm) =>
        {
            lock (navigated) navigated.Add(vm!);
        };

        var navTask = _navigationService.NavigateToAsync<SlowToTokenViewModel>();
        SlowToTokenViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowToTokenViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A same-type navigation without refresh targets the page that is
        // still activating: it is a no-op, so it completes true WITHOUT
        // cancelling the activation token the in-flight To is holding. (Its
        // fate completes while the To is still blocked — the request is
        // consumed, not left pending for a departure.)
        var selfNav = _navigationService.NavigateToAsync<SlowToTokenViewModel>();
        Assert.True(await selfNav.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(page.ToToken.IsCancellationRequested);

        page.ReleaseTo.TrySetResult();
        Assert.True(await navTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(page.ToCompleted);
        Assert.False(page.ToToken.IsCancellationRequested);
        Assert.Same(page, _navigationService.GetCurrentViewModel());
        lock (navigated) Assert.Single(navigated);
    }

    [Fact]
    public async Task Window_DifferentNavigationDuringSlowTo_CancelsActivationTokenAndDeparts()
    {
        var navTask = _navigationService.NavigateToAsync<SlowToTokenViewModel>();
        SlowToTokenViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowToTokenViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A navigation to a different page is a committed departure: the
        // activating page's token is cancelled so its To can stop promptly,
        // and the departure proceeds once the To finishes.
        var nav2 = _navigationService.NavigateToAsync<TestHomeViewModel>();
        await WaitUntilAsync(() => page!.ToToken.IsCancellationRequested);

        page.ReleaseTo.TrySetResult();
        Assert.True(await nav2.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await navTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        Assert.True(page.ToCompleted);
    }

    [Fact]
    public async Task Window_GuardVetoAfterInterruptedTo_RenewsActivationToken()
    {
        // Wait for the page to be surfaced as resting before navigating on:
        // an intent registered while the veto exit is still draining the
        // slot would be drained as moot along with the vetoed chain.
        var rested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.RegionNavigated += (_, vm) =>
        {
            if (vm is SlowToVetoViewModel) rested.TrySetResult();
        };

        var navTask = _navigationService.NavigateToAsync<SlowToVetoViewModel>();
        SlowToVetoViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowToVetoViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The departure to Home interrupts the activation (its token is
        // cancelled to close the window), but the guard vetoes it: the page
        // stays. Its lifecycle source must be renewed, so the From of a later
        // successful departure runs under a different token source than the
        // one the interrupted To captured.
        var nav2 = _navigationService.NavigateToAsync<TestHomeViewModel>();
        await WaitUntilAsync(() => page!.ToToken.IsCancellationRequested);
        page.ReleaseTo.TrySetResult();

        Assert.False(await nav2.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await navTask.WaitAsync(TimeSpan.FromSeconds(10)));
        await rested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(page, _navigationService.GetCurrentViewModel());

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        Assert.True(page.FromRan);
        Assert.False(page.FromToken.Equals(page.ToToken));
    }

    [Fact]
    public async Task Window_CancelAfterClaimDuringGuard_RenewsActivationToken()
    {
        var rested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.RegionNavigated += (_, vm) =>
        {
            if (vm is SlowToBlockingGuardViewModel) rested.TrySetResult();
        };

        var navTask = _navigationService.NavigateToAsync<SlowToBlockingGuardViewModel>();
        SlowToBlockingGuardViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowToBlockingGuardViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The request is claimed as the departure candidate (the claim
        // cancels the activating page's token) and its guard starts running.
        // Cancelling the caller's token now aborts the departure in the guard
        // phase: the page stays, and its lifecycle source must have been
        // renewed — a later successful departure's From runs under a
        // different token source than the one the interrupted To captured.
        using var cts = new CancellationTokenSource();
        var nav2 = _navigationService.NavigateToAsync<TestHomeViewModel>(cancellationToken: cts.Token);
        await WaitUntilAsync(() => page!.ToToken.IsCancellationRequested);
        page.ReleaseTo.TrySetResult();

        await page.GuardStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await nav2.WaitAsync(TimeSpan.FromSeconds(10)));
        page.ReleaseGuard();

        Assert.True(await navTask.WaitAsync(TimeSpan.FromSeconds(10)));
        await rested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(page, _navigationService.GetCurrentViewModel());

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
        Assert.True(page.FromRan);
        Assert.False(page.FromToken.Equals(page.ToToken));
    }

    [Fact]
    public async Task Window_WithdrawnRegistrationDuringSlowTo_ToExceptionStillReachesCaller()
    {
        var navTask = _navigationService.NavigateToAsync<SlowThrowingToViewModel>();
        SlowThrowingToViewModel? page = null;
        await WaitUntilAsync(() =>
        {
            page = _navigationService.GetCurrentViewModel() as SlowThrowingToViewModel;
            return page is not null;
        });
        await page!.ToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var withdrawn = _navigationService.NavigateToAsync<ChainLandingViewModel>(cancellationToken: cts.Token);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await withdrawn);

        await Task.Delay(500);
        Assert.False(navTask.IsCompleted);

        // The To now throws: the exception must fault the original caller's
        // task, not vanish into an error list that was already consumed.
        page.ReleaseTo.TrySetResult();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await navTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Init failed.", ex.Message);
    }

    [Fact]
    public async Task Window_LandingChainedDepartureVetoed_SurfacesPageWhenItRests()
    {
        ChainLandingViewModel.Reset();
        object? surfaced = null;
        var surfacedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.RegionNavigated += (_, vm) =>
        {
            if (vm is LandingVetoViewModel)
            {
                surfaced = vm;
                surfacedSignal.TrySetResult();
            }
        };

        // The page's To registers a follow-up request; the entering step hands
        // it straight to a departure (no landing raise — a request is pending),
        // and the page's own guard vetoes that departure. The page therefore
        // comes to rest as the current page and must be surfaced now.
        var result = await _navigationService.NavigateToAsync<LandingVetoViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        await surfacedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var current = Assert.IsType<LandingVetoViewModel>(_navigationService.GetCurrentViewModel());
        Assert.Same(current, surfaced);
        Assert.Equal(0, ChainLandingViewModel.ConstructionCount);
    }

    [Fact]
    public async Task Window_RegionNavigated_RaisedBeforeLandingCallerResumes()
    {
        var handlerRan = false;
        _navigationService.RegionNavigated += (_, vm) =>
        {
            if (vm is TestHomeViewModel)
            {
                handlerRan = true;
            }
        };

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        // The await resumed only after subscribers were notified.
        Assert.True(handlerRan);
    }

    [Fact]
    public async Task Window_WithdrawnRegistrationDuringSlowFrom_DoesNotStartGateClock()
    {
        var reported = new List<Exception>();
        _navigationService.SubscriberError += ex =>
        {
            lock (reported) reported.Add(ex);
        };

        await _navigationService.NavigateToAsync<SlowFromViewModel>();
        var slow = Assert.IsType<SlowFromViewModel>(_navigationService.GetCurrentViewModel());

        var candidate = _navigationService.NavigateToAsync<ButtonTargetViewModel>();
        await slow.FromStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Register and immediately withdraw a superseding request while From
        // is still in flight. The wake is void: nobody is superseding the
        // departure anymore, so the disposal-gate clock must not run against
        // the still-running From.
        using var cts = new CancellationTokenSource();
        var withdrawn = _navigationService.NavigateToAsync<ChainLandingViewModel>(cancellationToken: cts.Token);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await withdrawn);

        // Wait past the disposal-gate timeout (5s): with the gate clock
        // wrongly running, From would be abandoned here and the candidate
        // would execute while From is still blocked.
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.Same(slow, _navigationService.GetCurrentViewModel());
        lock (reported) Assert.DoesNotContain(reported, e => e is TimeoutException);

        slow.ReleaseFrom.TrySetResult();
        Assert.True(await candidate.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<ButtonTargetViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_HopCommitThrow_FaultsCaller_AndRegionStaysUsable()
    {
        await _navigationService.NavigateToAsync<CountingAwareViewModel>();

        // The target's attach throws inside the hop's commit section. The
        // exception must reach the caller (not wedge the region in the
        // Leaving phase), and the committed page is activated by recovery.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _navigationService.NavigateToAsync<ThrowingAttachViewModel>()
                .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Attach boom.", ex.Message);
        await WaitUntilAsync(() => _navigationService.GetCurrentViewModel() is ThrowingAttachViewModel);

        // The region is fully usable afterwards.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_PopToExistingAttachThrow_RetiredCtsCancellationStillSettles()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton<SecondAttachThrowingViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<SecondAttachThrowingViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));

        // Pop back to the singleton: the reactivation retires the entry's
        // previous CTS and then the attach throws inside the commit. The
        // retirement was committed under the lock, so its cancellation
        // must still run (and settle) even though the commit threw.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await nav.NavigateToAsync<SecondAttachThrowingViewModel>()
                .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Second attach boom.", ex.Message);

        // The retired source was cancelled and settled by the finally, so
        // the next reclaim pass on this entry disposes it: the retired list
        // drains instead of holding the source forever.
        await WaitUntilAsync(() =>
            GetRetiredCtsCount(nav, "MainRegion", typeof(SecondAttachThrowingViewModel)) == 0);
    }

    [Fact]
    public async Task Window_FirstNavigationCommitThrow_FaultsCaller_AndRegionStaysUsable()
    {
        // The same commit throw as the departure-path test, but as the very
        // first navigation into an empty region: no departing page exists,
        // and the caller's fate must still be faulted rather than left
        // incomplete (which would hang the caller forever).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _navigationService.NavigateToAsync<ThrowingAttachViewModel>()
                .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Attach boom.", ex.Message);
        await WaitUntilAsync(() => _navigationService.GetCurrentViewModel() is ThrowingAttachViewModel);

        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_RefreshWithFollowUpRegistered_NeverSurfacesTheIntermediatePage()
    {
        RefreshChainViewModel.Reset();
        ChainLandingViewModel.Reset();
        var raised = new List<INavigationViewModel>();
        _navigationService.RegionNavigated += (_, vm) =>
        {
            lock (raised) raised.Add(vm);
        };

        // The page lands (raise deferred: its To registered a refresh), the
        // refresh's To registers a follow-up, and only the follow-up's target
        // ever comes to rest. RegionNavigated must fire exactly once — for
        // the final page. The refresh path suppresses its own raise because
        // a further intent is pending; the departure wrapper must apply the
        // same rests test instead of surfacing the page anyway.
        Assert.True(await _navigationService.NavigateToAsync<RefreshChainViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => _navigationService.GetCurrentViewModel() is ChainLandingViewModel);
        await Task.Delay(300);

        List<INavigationViewModel> snapshot;
        lock (raised) snapshot = [.. raised];
        var single = Assert.Single(snapshot);
        Assert.IsType<ChainLandingViewModel>(single);
    }

    [Fact]
    public async Task Window_FollowUpWithdrawnAfterRefresh_SurfacesPageAtSettle()
    {
        SettleSurfaceViewModel.Reset();
        ChainLandingViewModel.Reset();
        var surfaced = new TaskCompletionSource<INavigationViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationService.RegionNavigated += (_, vm) =>
        {
            if (vm is SettleSurfaceViewModel)
            {
                surfaced.TrySetResult(vm);
            }
        };

        // Land (raise deferred: the To registered a refresh) and wait until
        // the refresh's To has registered its follow-up request and blocked
        // on the test's gate.
        Assert.True(await _navigationService.NavigateToAsync<SettleSurfaceViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        var page = Assert.IsType<SettleSurfaceViewModel>(_navigationService.GetCurrentViewModel());
        await page.RefreshToStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Withdraw the follow-up while it sits in the slot. Every earlier
        // raise point now defers: the refresh's rests check sees the slot
        // occupied, and the departure wrapper sees it too — so the page can
        // only be surfaced by the pump's settle branch, after the open step
        // discards the withdrawn request and finds nothing left to do.
        page.FollowUpCts.Cancel();
        page.ReleaseRefreshTo.TrySetResult();
        var raised = await surfaced.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(page, raised);
        Assert.Same(page, _navigationService.GetCurrentViewModel());
        Assert.Equal(0, ChainLandingViewModel.ConstructionCount);
    }

    [Fact]
    public async Task Window_CallbackRedirectOverOutsideCandidate_CountsAsChainHop()
    {
        ChainMidViewModel.Reset();
        ChainEndViewModel.Reset();
        using var svc = new NavigationService(_serviceProvider, new NavigationOptions { MaxNavigationChainLength = 1 });
        var limitError = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscriberError += ex =>
        {
            if (ex is InvalidOperationException && ex.Message.Contains("Navigation chain exceeded"))
            {
                limitError.TrySetResult();
            }
        };

        await svc.NavigateToAsync<ChainStartViewModel>();
        // Outside candidate: the departure it starts is superseded during
        // From by the page's own callback redirect (the caller's fate is
        // therefore false — superseded), but the executed hop is
        // callback-driven, so it must count as chain hop 1 — with a limit
        // of 1, the mid page's own follow-up request then trips the limit.
        Assert.False(await svc.NavigateToAsync<ButtonTargetViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));

        await limitError.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, ChainMidViewModel.ConstructionCount);
        Assert.Equal(0, ChainEndViewModel.ConstructionCount);
        Assert.IsType<ChainMidViewModel>(svc.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_OutsideRequestInSlotAtChainLimit_SurvivesAndExecutes()
    {
        RescueEndViewModel.Reset();
        using var svc = new NavigationService(_serviceProvider, new NavigationOptions { MaxNavigationChainLength = 1 });
        var limitError = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SubscriberError += ex =>
        {
            if (ex is InvalidOperationException && ex.Message.Contains("Navigation chain exceeded"))
            {
                limitError.TrySetResult();
            }
        };

        // Land the start page; its To registers the mid page (chain hop 1,
        // exhausting the limit of 1). The mid page's To registers the end
        // page, whose departure starts with the mid page's blocking guard.
        Assert.True(await svc.NavigateToAsync<RescueStartViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => svc.GetCurrentViewModel() is RescueMidViewModel);
        var mid = Assert.IsType<RescueMidViewModel>(svc.GetCurrentViewModel());
        await mid.GuardStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // While the doomed chain departure is still in its guard, an outside
        // request arrives. When the guard passes and the limit stops the
        // chain, the outside request must not be drained as collateral: it
        // stays pending and executes as a fresh departure.
        var outside = svc.NavigateToAsync<TestHomeViewModel>();
        mid.ReleaseGuard();

        await limitError.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await outside.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TestHomeViewModel>(svc.GetCurrentViewModel());
        Assert.Equal(0, RescueEndViewModel.ConstructionCount);
    }

    [Fact]
    public async Task Window_DetachThrowDuringMultiPop_SalvagesEveryPoppedScope()
    {
        StackBaseViewModel.Reset();
        StackMidViewModel.Reset();
        StackTopViewModel.Reset();
        DetachThrowingViewModel.Reset();

        // The base page is KeepAlive so the final navigation to its type
        // takes the pop-to-existing path (type match) instead of pushing a
        // fresh instance. The throwing page sits in the middle of the
        // stack, so in pop order it is neither first nor last: the pages
        // queued before AND after it must both survive its throw.
        await _navigationService.NavigateToAsync<StackBaseViewModel>(retention: NavigationRetention.KeepAlive);
        await _navigationService.NavigateToAsync<StackMidViewModel>();
        await _navigationService.NavigateToAsync<DetachThrowingViewModel>();
        await _navigationService.NavigateToAsync<StackTopViewModel>();
        DetachThrowingViewModel.ThrowOnDetach = true;

        // Pop-to-existing back to the base: the commit pops the top page
        // (queued normally), then the detach-throwing page, then the mid
        // page. The throw must fault the caller, and ALL THREE popped
        // pages' scopes must still be disposed — without the salvage, the
        // loop aborts at the throwing entry and its own scope (and, in a
        // break-after-salvage regression, the mid page's) is simply lost.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _navigationService.NavigateToAsync<StackBaseViewModel>()
                .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Detach boom.", ex.Message);

        await WaitUntilAsync(() => StackTopViewModel.DisposedCount == 1
            && DetachThrowingViewModel.DisposedCount == 1
            && StackMidViewModel.DisposedCount == 1);
        Assert.IsType<StackBaseViewModel>(_navigationService.GetCurrentViewModel());

        // The region is fully usable afterwards.
        Assert.True(await _navigationService.NavigateToAsync<TestHomeViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Remove_DetachThrowOnMatchedPages_StillRemovesAndDisposesEveryMatch()
    {
        StackMidViewModel.Reset();
        StackTopViewModel.Reset();
        DetachThrowingViewModel.Reset();

        // Two covered instances of the throwing type with a normal page
        // between them, under an unmatched top page. The stack is already
        // rewritten when the removal loop runs, so the first detach throw
        // must not abort the batch: the second match's deactivation plan
        // must still be built and executed.
        await _navigationService.NavigateToAsync<DetachThrowingViewModel>();
        await _navigationService.NavigateToAsync<StackMidViewModel>();
        await _navigationService.NavigateToAsync<DetachThrowingViewModel>();
        await _navigationService.NavigateToAsync<StackTopViewModel>();
        DetachThrowingViewModel.ThrowOnDetach = true;

        var ex = Assert.Throws<AggregateException>(() => _navigationService.Remove<DetachThrowingViewModel>());
        Assert.Equal(2, ex.InnerExceptions.Count);

        // Both matched scopes were disposed; the unmatched pages remain.
        Assert.Equal(2, DetachThrowingViewModel.DisposedCount);
        Assert.Equal(0, StackMidViewModel.DisposedCount);
        Assert.Equal(0, StackTopViewModel.DisposedCount);
        Assert.Equal(
            [typeof(StackMidViewModel), typeof(StackTopViewModel)],
            _navigationService.GetNavigationStack().Select(vm => vm.GetType()).ToArray());
        Assert.IsType<StackTopViewModel>(_navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Dispose_DetachThrowOnOnePage_StillDisposesEveryScope()
    {
        StackMidViewModel.Reset();
        StackTopViewModel.Reset();
        DetachThrowingViewModel.Reset();

        await _navigationService.NavigateToAsync<DetachThrowingViewModel>();
        await _navigationService.NavigateToAsync<StackMidViewModel>();
        await _navigationService.NavigateToAsync<StackTopViewModel>();
        DetachThrowingViewModel.ThrowOnDetach = true;

        // The detach exception is reported only after the shutdown has run
        // to completion. Without per-item protection the throw escapes the
        // detach loop and the token cancellations and scope disposals
        // below it never run — and the service is already marked disposed,
        // so nothing would ever retry them.
        var svc = (NavigationService)_navigationService;
        var ex = Assert.Throws<InvalidOperationException>(() => svc.Dispose());
        Assert.Equal("Detach boom.", ex.Message);
        Assert.Equal(1, DetachThrowingViewModel.DisposedCount);
        Assert.Equal(1, StackMidViewModel.DisposedCount);
        Assert.Equal(1, StackTopViewModel.DisposedCount);

        // A second Dispose is a no-op: it neither throws nor disposes again.
        svc.Dispose();
        Assert.Equal(1, DetachThrowingViewModel.DisposedCount);
        Assert.Equal(1, StackMidViewModel.DisposedCount);
        Assert.Equal(1, StackTopViewModel.DisposedCount);
    }

    [Fact]
    public async Task DisposeAsync_DetachThrowOnOnePage_StillDisposesEveryScope()
    {
        StackMidViewModel.Reset();
        StackTopViewModel.Reset();
        DetachThrowingViewModel.Reset();

        await _navigationService.NavigateToAsync<DetachThrowingViewModel>();
        await _navigationService.NavigateToAsync<StackMidViewModel>();
        await _navigationService.NavigateToAsync<StackTopViewModel>();
        DetachThrowingViewModel.ThrowOnDetach = true;

        var svc = (NavigationService)_navigationService;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await svc.DisposeAsync());
        Assert.Equal("Detach boom.", ex.Message);
        Assert.Equal(1, DetachThrowingViewModel.DisposedCount);
        Assert.Equal(1, StackMidViewModel.DisposedCount);
        Assert.Equal(1, StackTopViewModel.DisposedCount);

        // A second DisposeAsync is a no-op: it neither throws nor disposes again.
        await svc.DisposeAsync();
        Assert.Equal(1, DetachThrowingViewModel.DisposedCount);
        Assert.Equal(1, StackMidViewModel.DisposedCount);
        Assert.Equal(1, StackTopViewModel.DisposedCount);
    }

    [Fact]
    public async Task Window_DetachThrowWhileCovered_KeepsThePageAlive()
    {
        DetachThrowingViewModel.Reset();

        // The throwing page is the region's current page (KeepAlive);
        // pushing another page over it merely COVERS it (destroy: false)
        // — and its detach throws during that commit. The covered page
        // stays on the stack and must NOT be marked for disposal: the
        // salvage has to honor the destroy flag, or the page's later
        // KeepAlive pop would dispose its scope instead of caching it.
        await _navigationService.NavigateToAsync<StackBaseViewModel>(retention: NavigationRetention.KeepAlive);
        await _navigationService.NavigateToAsync<DetachThrowingViewModel>(retention: NavigationRetention.KeepAlive);
        DetachThrowingViewModel.ThrowOnDetach = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _navigationService.NavigateToAsync<StackTopViewModel>()
                .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Detach boom.", ex.Message);
        Assert.IsType<StackTopViewModel>(_navigationService.GetCurrentViewModel());

        // Back to the covered page: it is still the same live instance.
        DetachThrowingViewModel.ThrowOnDetach = false;
        Assert.True(await _navigationService.GoBackAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<DetachThrowingViewModel>(_navigationService.GetCurrentViewModel());

        // Pop the throwing page itself (KeepAlive): with the destroy flag
        // honored, no pending disposal was ever recorded for its scope,
        // so the pop caches the page instead of disposing it.
        Assert.True(await _navigationService.GoBackAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<StackBaseViewModel>(_navigationService.GetCurrentViewModel());
        Assert.Equal(0, DetachThrowingViewModel.DisposedCount);
    }

    [Fact]
    public async Task Window_DetachThrowLanding_RecoveredPageGateIsBalanced()
    {
        StackBaseViewModel.Reset();
        StackTopViewModel.Reset();
        DetachThrowingViewModel.Reset();
        var gateTimeouts = new List<Exception>();
        _navigationService.SubscriberError += ex =>
        {
            if (ex is TimeoutException)
            {
                lock (gateTimeouts) gateTimeouts.Add(ex);
            }
        };

        await _navigationService.NavigateToAsync<StackBaseViewModel>(retention: NavigationRetention.KeepAlive);
        await _navigationService.NavigateToAsync<DetachThrowingViewModel>(retention: NavigationRetention.KeepAlive);
        DetachThrowingViewModel.ThrowOnDetach = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _navigationService.NavigateToAsync<StackTopViewModel>()
                .WaitAsync(TimeSpan.FromSeconds(10)));
        DetachThrowingViewModel.ThrowOnDetach = false;

        // Pop the recovered page for destroy: its scope disposal waits on
        // its in-flight gate. If the commit-time registration leaked on the
        // detach-throw path, the gate never completes — the disposal waits
        // out the full gate timeout and reports a spurious TimeoutException
        // blaming the page's lifecycle, BEFORE the disposal proceeds. So by
        // the time the scope is observed disposed, a leaked gate has
        // necessarily already reported: the emptiness assertion below is
        // the discriminator, with no wall-clock threshold involved.
        Assert.True(await _navigationService.GoBackAsync().WaitAsync(TimeSpan.FromSeconds(15)));
        await WaitUntilAsync(() => StackTopViewModel.DisposedCount == 1);
        lock (gateTimeouts) Assert.Empty(gateTimeouts);
    }

    [Fact]
    public async Task CrossInstance_CallFromCallback_IsAnOutsideCallForTheOtherInstance()
    {
        CrossCallerViewModel.Reset();
        using var svcA = new NavigationService(_serviceProvider);
        using var svcB = new NavigationService(_serviceProvider);

        // Instance B rests on a page whose guard vetoes departures.
        await svcB.NavigateToAsync<VetoGuardViewModel>();
        var guard = Assert.IsType<VetoGuardViewModel>(svcB.GetCurrentViewModel());
        guard.Veto = true;

        // From instance A's page callback, a call into B is awaited. For B
        // it is an outside call with a real fate: the veto must be observed
        // as false — not swallowed by an in-callback classification (which
        // would return an already-completed true).
        CrossCallerViewModel.OtherService = svcB;
        await svcA.NavigateToAsync<CrossCallerViewModel>();
        await CrossCallerViewModel.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(false, CrossCallerViewModel.ObservedResult);
    }

    [Fact]
    public async Task Dispose_DuringInFlightGuard_SettlesTheDepartingCallerFalse()
    {
        await _navigationService.NavigateToAsync<BlockingGuardViewModel>();
        var guardPage = Assert.IsType<BlockingGuardViewModel>(_navigationService.GetCurrentViewModel());

        var candidate = _navigationService.NavigateToAsync<TestHomeViewModel>();
        await guardPage.GuardStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The guard never completes on its own; disposal must still settle
        // the departing caller instead of leaving it hanging past Dispose.
        ((IDisposable)_navigationService).Dispose();
        Assert.False(await candidate.WaitAsync(TimeSpan.FromSeconds(10)));
        guardPage.ReleaseGuard();
    }

    [Fact]
    public async Task Window_Refresh_ConsultsGuard_AndVetoLeavesPageUntouched()
    {
        await _navigationService.NavigateToAsync<VetoGuardViewModel>();
        var guard = Assert.IsType<VetoGuardViewModel>(_navigationService.GetCurrentViewModel());
        Assert.Equal(1, guard.NavigatedToCount);

        // A refresh is not a departure, but the leave guard is still consulted
        // for it (a same-type no-op without refresh is not — it short-circuits
        // before the guard).
        guard.Veto = true;
        var vetoed = await _navigationService.NavigateToAsync<VetoGuardViewModel>(refreshIfActive: true)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(vetoed);
        Assert.Equal(1, guard.NavigatedToCount);

        guard.Veto = false;
        var refreshed = await _navigationService.NavigateToAsync<VetoGuardViewModel>(refreshIfActive: true)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(refreshed);
        Assert.Equal(2, guard.NavigatedToCount);
        Assert.Same(guard, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Window_InCallbackNavigationCall_ReturnsAlreadyCompletedTask()
    {
        InCallbackTaskStateViewModel.Reset();
        var rest = RestSignalFor<ChainLandingViewModel>();

        var result = await _navigationService.NavigateToAsync<InCallbackTaskStateViewModel>()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
        await rest.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Registration from inside a lifecycle callback completes the returned
        // task immediately (the request's fate is decided at the window edge).
        Assert.True(InCallbackTaskStateViewModel.CallbackTaskWasCompleted);
        Assert.IsType<ChainLandingViewModel>(_navigationService.GetCurrentViewModel());
    }

    private sealed class ConcurrentGuardViewModel : NavigationViewModelBase, INavigationGuard
    {
        public readonly TaskCompletionSource GuardEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _guardRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
        {
            GuardEntered.TrySetResult();
            await _guardRelease.Task.ConfigureAwait(false);
            return true;
        }

        public void ReleaseGuard() => _guardRelease.TrySetResult();
    }

    private sealed class ConcurrentTargetViewModel : NavigationViewModelBase, IDisposable
    {
        public static int DisposeCount;

        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCount);
        }
    }

    [Fact]
    public async Task DisposeAsync_DuringDeferredDisposal_DisposesScopeExactlyOnce()
    {
        // A scope transferred to deferred disposal (self-dispose via reentrant
        // navigation) must not be collected by TakeAllScopesForDisposal: otherwise
        // DisposeAsync and the deferred task would double-dispose it.
        // Scenario: KeepAlive page in stack+cache -> ClearCache moves scope to
        // _pendingScopeDisposals -> reentrant navigation removes last stack ref
        // (deferred disposal takes ownership) -> concurrent DisposeAsync.
        DeferredDisposeViewModel.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<DeferredDisposeViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<DeferredDisposeViewModel>(retention: NavigationRetention.KeepAlive);
        var vm = Assert.IsType<DeferredDisposeViewModel>(nav.GetCurrentViewModel());
        vm.SetNavigation(nav);

        // Cache cleared while still referenced by the stack: scope enters pending.
        nav.ClearCache("MainRegion");

        // Start navigating away; the async OnNavigatedFrom blocks and triggers
        // a reentrant Replace that destroys this page while its callback runs
        // (self-dispose -> deferred disposal takes ownership).
        var navTask = Task.Run(() => nav.NavigateToAsync<TestDetailViewModel>());
        await vm.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The reentrant navigation has run; the scope is now under deferred disposal.
        // DisposeAsync must not collect it (would double-dispose when the deferred
        // task fires). It waits for the deferred task instead.
        var disposeTask = Task.Run(async () =>
        {
            await ((IAsyncDisposable)nav).DisposeAsync();
        });

        // Let the callback finish; both paths now complete.
        vm.ReleaseCallback();
        await navTask.WaitAsync(TimeSpan.FromSeconds(10));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, DeferredDisposeViewModel.DisposeCount);
    }

    private sealed class DeferredDisposeViewModel : NavigationViewModelBase, INavigationAwareAsync, IDisposable
    {
        public static int DisposeCount;
        public readonly TaskCompletionSource CallbackEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private INavigationService? _nav;
        private bool _reentered;

        public void SetNavigation(INavigationService nav) => _nav = nav;

        public async Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        public async Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            if (!_reentered)
            {
                _reentered = true;
                CallbackEntered.TrySetResult();
                // Reentrant navigation destroying this page while its callback runs.
                if (_nav is not null)
                {
                    await _nav.NavigateToAsync<TestDetailViewModel>(operation: NavigationOperation.Replace);
                }
            }
            await _release.Task.ConfigureAwait(false);
        }

        public void ReleaseCallback() => _release.TrySetResult();

        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCount);
        }
    }

    [Fact]
    public async Task DisposeAsync_PendingScopeEntryGateOpen_WaitsForGateBeforeDisposingScope()
    {
        // A scope whose disposal is recorded as pending (its last cache/stack
        // reference went away while a destroy was outstanding) is collected by
        // DisposeAsync through the pending records — its entry is on no stack
        // and in no cache anymore. If that entry still has a lifecycle callback
        // in flight, DisposeAsync must cancel and wait for its gate exactly
        // like for stacked entries, or it releases the scope while the callback
        // still uses it. The pending state is staged directly (reflection):
        // the record a deactivation plan leaves behind for a popped entry,
        // with the entry's gate held open.
        PendingGateViewModel.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<PendingGateViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<PendingGateViewModel>(retention: NavigationRetention.KeepAlive);
        var entry = GetNavigationEntry(nav, "MainRegion", typeof(PendingGateViewModel));
        Assert.NotNull(entry);
        var entryType = entry.GetType();
        var scope = entryType.GetProperty("Scope")!.GetValue(entry)!;
        var viewModel = entryType.GetProperty("ViewModel")!.GetValue(entry)!;

        // Detach the entry from its stack (the state a completed hop leaves
        // behind) and record its scope as pending, as BuildDeactivatePlan does
        // for a destroyed entry whose scope outlives its stack presence.
        var stacksField = typeof(NavigationService).GetField("_regionStacks",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var stacks = (System.Collections.IDictionary)stacksField.GetValue(nav)!;
        var existingStack = stacks["MainRegion"]!;
        var rebuilt = Activator.CreateInstance(existingStack.GetType())!;
        stacks["MainRegion"] = rebuilt;

        var pendingType = typeof(NavigationService).GetNestedType("PendingDisposal",
            BindingFlags.NonPublic)!;
        var entriesList = Activator.CreateInstance(
            typeof(List<>).MakeGenericType(entryType))!;
        ((System.Collections.IList)entriesList).Add(entry);
        var record = Activator.CreateInstance(pendingType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [viewModel, new HashSet<string> { "MainRegion" }, entriesList],
            culture: null)!;
        var pendingField = typeof(NavigationService).GetField("_pendingScopeDisposals",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var pending = (System.Collections.IDictionary)pendingField.GetValue(nav)!;
        pending[scope] = record;

        // A lifecycle callback of the pending entry is still in flight.
        var enterMethod = entryType.GetMethod("EnterInFlightLifecycle")!;
        var gate = (Task)enterMethod.Invoke(entry, null)!;

        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeTask = Task.Run(async () =>
        {
            disposeStarted.TrySetResult();
            await ((IAsyncDisposable)nav).DisposeAsync();
        });

        // DisposeAsync must wait for the pending entry's gate before releasing
        // its scope: no completion and no disposal while the gate is open.
        // The wait is staged on the started signal plus a bounded window so
        // the assertion cannot pass vacuously on a starved scheduler.
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var completedFirst = await Task.WhenAny(disposeTask, Task.Delay(300));
        Assert.NotSame(disposeTask, completedFirst);
        Assert.Equal(0, PendingGateViewModel.DisposeCount);

        var exitMethod = entryType.GetMethod("ExitInFlightLifecycle")!;
        exitMethod.Invoke(entry, new object[] { gate });
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, PendingGateViewModel.DisposeCount);
    }

    private sealed class PendingGateViewModel : NavigationViewModelBase, IDisposable
    {
        public static int DisposeCount;

        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_InFlightCallback_ScopeDisposedOnlyAfterCallbackCompletes()
    {
        // DisposeAsync's declared semantics: in-flight lifecycle callbacks are
        // waited for (with a timeout) before their page scopes are released.
        // The exactly-once tests cannot catch an ordering violation, so this
        // test asserts the scope is untouched while the callback is blocked
        // and that disposal happens only after it completed.
        GateOrderingViewModel.DisposeCount = 0;
        GateOrderingViewModel.DisposedBeforeCallbackRelease = false;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<GateOrderingViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<GateOrderingViewModel>();
        var vm = Assert.IsType<GateOrderingViewModel>(nav.GetCurrentViewModel());

        var navTask = Task.Run(() => nav.NavigateToAsync<TestDetailViewModel>());
        await vm.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeTask = Task.Run(async () =>
        {
            disposeStarted.TrySetResult();
            await ((IAsyncDisposable)nav).DisposeAsync();
        });

        // The From callback is still running: its page scope must not have
        // been released underneath it. Staged on the started signal plus a
        // bounded window, like the pending-gate test above.
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var completedFirst = await Task.WhenAny(disposeTask, Task.Delay(300));
        Assert.NotSame(disposeTask, completedFirst);
        Assert.Equal(0, GateOrderingViewModel.DisposeCount);

        vm.ReleaseCallback();
        await navTask.WaitAsync(TimeSpan.FromSeconds(10));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, GateOrderingViewModel.DisposeCount);
        Assert.False(GateOrderingViewModel.DisposedBeforeCallbackRelease);
    }

    private sealed class GateOrderingViewModel : NavigationViewModelBase, INavigationAwareAsync, IDisposable
    {
        public static int DisposeCount;
        public static bool DisposedBeforeCallbackRelease;
        public readonly TaskCompletionSource CallbackEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            CallbackEntered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }

        public void ReleaseCallback() => _release.TrySetResult();

        public void Dispose()
        {
            DisposedBeforeCallbackRelease = !_release.Task.IsCompleted;
            Interlocked.Increment(ref DisposeCount);
        }
    }

    [Fact]
    public async Task DisposeAsync_DeferredDisposalRegisteredDuringDrain_IsAwaitedBeforeReturn()
    {
        // Shutdown drains deferred scope disposals until none are in flight:
        // a deferral registered while DisposeAsync is already waiting (work
        // that was in flight when disposal began — staged here by invoking
        // the tracker directly, as a self-disposing page would) must also
        // complete before DisposeAsync returns. With a one-time snapshot,
        // the second deferral would be missed and DisposeAsync would return
        // as soon as the first one finished.
        DrainProbe.DisposeCount = 0;
        DrainProbe.OnDisposed = null;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddScoped<DrainProbe>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        var trackMethod = typeof(NavigationService).GetMethod("TrackDeferredDisposal",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var scopes = new List<IServiceScope>();
        void RegisterDeferred(TaskCompletionSource gate)
        {
            var scope = scopeFactory.CreateScope();
            scopes.Add(scope);
            // Force the probe's creation so its disposal is observable.
            _ = scope.ServiceProvider.GetRequiredService<DrainProbe>();
            trackMethod.Invoke(nav, [gate.Task, scope, new TestHomeViewModel(), "MainRegion", null]);
        }

        // First deferral: registered before disposal, its gate holds it open.
        var gate1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisterDeferred(gate1);

        // The second deferral is registered from inside the first scope's
        // disposal: that runs on the first deferral's own task, while the
        // drain is parked awaiting exactly that task (see the staging
        // below) and before the task completes — so once the drain is
        // parked, the registration lands strictly after its first snapshot.
        var gate2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DrainProbe.OnDisposed = () =>
        {
            // One-shot: the second scope's disposal must not register a third.
            DrainProbe.OnDisposed = null;
            RegisterDeferred(gate2);
        };

        try
        {
            var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposeTask = Task.Run(async () =>
            {
                disposeStarted.TrySetResult();
                await ((IAsyncDisposable)nav).DisposeAsync();
            });

            // Park DisposeAsync in the drain first: everything between the
            // started signal and the drain in an empty service is synchronous
            // in-memory work, and the drain cannot pass the first deferral
            // while its gate is closed. Only then release the gate.
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(300);

            // Releasing the first gate finishes the first deferral, which
            // registers the second one on its way. DisposeAsync must not
            // return yet: the drain has to pick up the second deferral.
            gate1.TrySetResult();
            var completedFirst = await Task.WhenAny(disposeTask, Task.Delay(300));
            Assert.NotSame(disposeTask, completedFirst);
            Assert.Equal(1, DrainProbe.DisposeCount);

            // Releasing the second gate lets the drain empty and DisposeAsync return.
            gate2.TrySetResult();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, DrainProbe.DisposeCount);
        }
        finally
        {
            DrainProbe.OnDisposed = null;
        }
    }

    private sealed class DrainProbe : IDisposable
    {
        public static int DisposeCount;
        public static Action? OnDisposed;

        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCount);
            OnDisposed?.Invoke();
        }
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCall_JoinsInFlightDisposal()
    {
        // Concurrent DisposeAsync calls share one disposal: the first call
        // performs the cleanup, and a call that arrives while it is in
        // flight must not complete before the cleanup has finished — the
        // documented completion guarantee belongs to every caller, not
        // only the one doing the work. The first call is parked
        // deterministically inside the page's own DisposeAsync.
        BlockingAsyncDisposeViewModel.DisposeAsyncCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<BlockingAsyncDisposeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<BlockingAsyncDisposeViewModel>();
        var vm = Assert.IsType<BlockingAsyncDisposeViewModel>(nav.GetCurrentViewModel());

        var first = Task.Run(async () => await ((IAsyncDisposable)nav).DisposeAsync());
        await vm.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(async () =>
        {
            secondStarted.TrySetResult();
            await ((IAsyncDisposable)nav).DisposeAsync();
        });
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var completedFirst = await Task.WhenAny(second, Task.Delay(300));
        Assert.NotSame(second, completedFirst);

        vm.ReleaseDisposal();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        await second.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, BlockingAsyncDisposeViewModel.DisposeAsyncCount);

        // After completion, further calls return immediately and do no work.
        var third = Task.Run(async () => await ((IAsyncDisposable)nav).DisposeAsync());
        await third.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, BlockingAsyncDisposeViewModel.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallers_ObserveSameCollectedError()
    {
        // Joining callers do not merely wait for the same completion: they
        // observe the same collected errors the first caller's cleanup
        // produced. The first disposal is parked inside the page's
        // DisposeAsync, the second joins, and both must surface the page's
        // failure — the same exception object, not two copies.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ThrowingDisposeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ThrowingDisposeViewModel>();
        var vm = Assert.IsType<ThrowingDisposeViewModel>(nav.GetCurrentViewModel());

        var first = Task.Run(async () => await ((IAsyncDisposable)nav).DisposeAsync());
        await vm.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(async () =>
        {
            secondStarted.TrySetResult();
            await ((IAsyncDisposable)nav).DisposeAsync();
        });
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        vm.ReleaseDisposal();
        var firstError = await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        var secondError = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Same(firstError, secondError);
    }

    private sealed class ThrowingDisposeViewModel : NavigationViewModelBase, IAsyncDisposable
    {
        public readonly TaskCompletionSource DisposalEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            DisposalEntered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            throw new InvalidOperationException("ThrowingDisposeViewModel disposal failed.");
        }

        public void ReleaseDisposal() => _release.TrySetResult();
    }

    [Fact]
    public async Task DisposeAsync_AfterSyncDispose_ReturnsImmediatelyAndPerformsNoCleanup()
    {
        // A synchronous Dispose performs the cleanup itself (synchronously,
        // with its weaker callback guarantee). A DisposeAsync that arrives
        // afterwards has nothing to join and nothing to do.
        DualDisposeViewModel.SyncDisposeCount = 0;
        DualDisposeViewModel.AsyncDisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<DualDisposeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<DualDisposeViewModel>();

        ((IDisposable)nav).Dispose();
        Assert.Equal(1, DualDisposeViewModel.SyncDisposeCount);
        Assert.Equal(0, DualDisposeViewModel.AsyncDisposeCount);

        await ((IAsyncDisposable)nav).DisposeAsync();
        Assert.Equal(1, DualDisposeViewModel.SyncDisposeCount);
        Assert.Equal(0, DualDisposeViewModel.AsyncDisposeCount);
    }

    private sealed class DualDisposeViewModel : NavigationViewModelBase, IDisposable, IAsyncDisposable
    {
        public static int SyncDisposeCount;
        public static int AsyncDisposeCount;

        public void Dispose() => Interlocked.Increment(ref SyncDisposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref AsyncDisposeCount);
            return default;
        }
    }

    [Fact]
    public async Task Dispose_SyncCallDuringAsyncDisposal_ReturnsWithoutWaiting()
    {
        // The synchronous Dispose never joins an asynchronous disposal: it
        // returns as soon as the service is marked disposed, even while
        // the asynchronous cleanup is parked inside a page's disposal.
        BlockingAsyncDisposeViewModel.DisposeAsyncCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<BlockingAsyncDisposeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<BlockingAsyncDisposeViewModel>();
        var vm = Assert.IsType<BlockingAsyncDisposeViewModel>(nav.GetCurrentViewModel());

        var disposeTask = Task.Run(async () => await ((IAsyncDisposable)nav).DisposeAsync());
        await vm.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var syncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncTask = Task.Run(() =>
        {
            syncStarted.TrySetResult();
            ((IDisposable)nav).Dispose();
        });
        await syncStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var syncFinishedFirst = await Task.WhenAny(syncTask, Task.Delay(300));
        Assert.Same(syncTask, syncFinishedFirst);

        vm.ReleaseDisposal();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, BlockingAsyncDisposeViewModel.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_FromCallbackWhileDisposalInFlight_ReturnsSilently()
    {
        // Once an asynchronous disposal has begun, it waits on the gates
        // of in-flight callbacks. A callback that itself calls DisposeAsync
        // in that window must neither be thrown into (its remaining code
        // still matters) nor made to await the operation that is waiting
        // on it: the call returns silently, as it did when the disposed
        // flag short-circuited every later call.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<ReentrantCallbackViewModel>();
        services.AddTransient<TestDetailViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<ReentrantCallbackViewModel>();
        var vm = Assert.IsType<ReentrantCallbackViewModel>(nav.GetCurrentViewModel());

        var navTask = Task.Run(() => nav.NavigateToAsync<TestDetailViewModel>());
        await vm.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var disposeTask = Task.Run(async () => await ((IAsyncDisposable)nav).DisposeAsync());

        // Wait until the disposal has actually begun — it clears the
        // current page when it claims the scopes — before letting the
        // callback call back into DisposeAsync.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (nav.GetCurrentViewModel() is not null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The disposal did not begin in time.");
            await Task.Delay(10);
        }

        vm.ReleaseCallback();
        var releasedAt = DateTime.UtcNow;
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
        await navTask.WaitAsync(TimeSpan.FromSeconds(10));
        var elapsed = DateTime.UtcNow - releasedAt;

        Assert.True(vm.InnerCallReturned);
        Assert.False(vm.InnerCallThrew);
        // The silent return is immediate: had the callback instead joined
        // the disposal that waits on it, the cleanup would only proceed
        // after the 5-second disposal-gate timeout (and report it).
        Assert.True(elapsed < TimeSpan.FromSeconds(4),
            $"The disposal took {elapsed}; the callback's call appears to have joined it.");
    }

    private sealed class ReentrantCallbackViewModel : NavigationViewModelBase, INavigationAwareAsync
    {
        public readonly TaskCompletionSource CallbackEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool InnerCallReturned { get; private set; }
        public bool InnerCallThrew { get; private set; }

        public Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            // Capture the service now: disposal detaches it (Navigation
            // becomes null) when it claims the scopes.
            var service = (NavigationService)Navigation!;
            CallbackEntered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            try
            {
                await service.DisposeAsync().ConfigureAwait(false);
                InnerCallReturned = true;
            }
            catch (InvalidOperationException)
            {
                InnerCallThrew = true;
            }
        }

        public void ReleaseCallback() => _release.TrySetResult();
    }

    private sealed class BlockingAsyncDisposeViewModel : NavigationViewModelBase, IAsyncDisposable
    {
        public static int DisposeAsyncCount;
        public readonly TaskCompletionSource DisposalEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            DisposalEntered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            Interlocked.Increment(ref DisposeAsyncCount);
        }

        public void ReleaseDisposal() => _release.TrySetResult();
    }

    [Fact]
    public async Task Recognition_CacheCandidateClearedDuringVote_VoteFailureReportedAndFreshInstancePushed()
    {
        // Target recognition is documented to run without serialization
        // against page disposal: a candidate's scope can be released while
        // its IsNavigationTarget vote is still executing. This test stages
        // that interleaving deterministically — the vote blocks, the cache
        // is cleared (releasing the candidate's scope), then the vote is
        // released and touches its captured scoped service. The framework
        // must not crash and must not end up inconsistent: the vote's
        // ObjectDisposedException is reported through SubscriberError, the
        // candidate counts as not recognized, a fresh instance is pushed,
        // and the released scope was disposed exactly once.
        BlockingVoteViewModel.DisposeCount = 0;
        VoteProbe.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<BlockingVoteViewModel>();
        services.AddTransient<TestDetailViewModel>();
        services.AddScoped<VoteProbe>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var subscriberErrors = new List<Exception>();
        nav.SubscriberError += ex =>
        {
            lock (subscriberErrors)
            {
                subscriberErrors.Add(ex);
            }
        };

        await nav.NavigateToAsync<BlockingVoteViewModel>(retention: NavigationRetention.KeepAlive);
        var candidate = Assert.IsType<BlockingVoteViewModel>(nav.GetCurrentViewModel());

        // Move the candidate into the KeepAlive cache.
        await nav.NavigateToAsync<TestDetailViewModel>(operation: NavigationOperation.Replace);

        // The next navigation classifies the cached candidate: its vote blocks.
        var navTask = Task.Run(() => nav.NavigateToAsync<BlockingVoteViewModel, object>(
            new object(), retention: NavigationRetention.KeepAlive));
        await candidate.VoteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            // While the vote is in flight, the candidate's cache entry is cleared
            // and its page scope is released.
            nav.ClearCache("MainRegion");
            Assert.Equal(1, VoteProbe.DisposeCount);
        }
        finally
        {
            // Never leave the vote blocked on a failed assertion path.
            candidate.ReleaseVote();
        }

        var result = await navTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result);
        var current = Assert.IsType<BlockingVoteViewModel>(nav.GetCurrentViewModel());
        Assert.NotSame(candidate, current);
        Assert.Equal(1, BlockingVoteViewModel.DisposeCount);
        Assert.Equal(1, VoteProbe.DisposeCount);
        lock (subscriberErrors)
        {
            var error = Assert.Single(subscriberErrors);
            Assert.IsType<ObjectDisposedException>(error);
        }
    }

    [Fact]
    public async Task Recognition_StackCandidateServiceDisposedDuringVote_VoteFailureReportedAndNavigationDiscarded()
    {
        // The stack-classification twin of the cache scenario: a covered
        // KeepAlive entry is being voted on when the navigation service
        // itself is disposed, releasing every page scope including the
        // candidate's. (Remove cannot play this role: removing a KeepAlive
        // entry caches it instead of disposing its scope.) Same
        // expectations: the vote's ObjectDisposedException is reported
        // through SubscriberError, the candidate counts as not recognized,
        // the in-flight navigation is discarded as disposed, and the
        // candidate's scope was disposed exactly once.
        BlockingVoteViewModel.DisposeCount = 0;
        VoteProbe.DisposeCount = 0;

        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<BlockingVoteViewModel>();
        services.AddTransient<TestDetailViewModel>();
        services.AddScoped<VoteProbe>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var subscriberErrors = new List<Exception>();
        nav.SubscriberError += ex =>
        {
            lock (subscriberErrors)
            {
                subscriberErrors.Add(ex);
            }
        };

        await nav.NavigateToAsync<BlockingVoteViewModel>(retention: NavigationRetention.KeepAlive);
        var candidate = Assert.IsType<BlockingVoteViewModel>(nav.GetCurrentViewModel());

        // Cover the candidate with another page; it stays on the stack.
        await nav.NavigateToAsync<TestDetailViewModel>();

        // The next navigation classifies the covered candidate: its vote blocks.
        var navTask = Task.Run(() => nav.NavigateToAsync<BlockingVoteViewModel, object>(
            new object(), retention: NavigationRetention.KeepAlive));
        await candidate.VoteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            // While the vote is in flight, the service is disposed: every page
            // scope, including the candidate's, is released.
            await ((IAsyncDisposable)nav).DisposeAsync();
            Assert.Equal(1, VoteProbe.DisposeCount);
        }
        finally
        {
            // Never leave the vote blocked on a failed assertion path.
            candidate.ReleaseVote();
        }

        var result = await navTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result);
        Assert.Null(nav.GetCurrentViewModel());
        // Two scopes were released, each exactly once: the candidate's (by
        // DisposeAsync above) and the discarded navigation's tentative
        // resolution scope (released when the navigation was dropped).
        Assert.Equal(2, BlockingVoteViewModel.DisposeCount);
        Assert.Equal(2, VoteProbe.DisposeCount);
        lock (subscriberErrors)
        {
            var error = Assert.Single(subscriberErrors);
            Assert.IsType<ObjectDisposedException>(error);
        }
    }

    private sealed class BlockingVoteViewModel : NavigationViewModelBase, INavigationTargetAware, IDisposable
    {
        public static int DisposeCount;
        private readonly VoteProbe _probe;
        private int _voteCalls;
        public readonly TaskCompletionSource VoteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _voteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingVoteViewModel(VoteProbe probe) => _probe = probe;

        public bool IsNavigationTarget(object? parameter)
        {
            if (Interlocked.Increment(ref _voteCalls) == 1)
            {
                VoteEntered.TrySetResult();
                _voteRelease.Task.GetAwaiter().GetResult();
            }

            // Touch a service captured from this page's own scope. If the
            // page was disposed while this vote was in flight, the scope is
            // gone and this throws ObjectDisposedException — the documented
            // hazard of voting from captured scoped services.
            _probe.Ping();
            return true;
        }

        public void ReleaseVote() => _voteRelease.TrySetResult();

        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    private sealed class VoteProbe : IDisposable
    {
        public static int DisposeCount;
        private volatile bool _disposed;

        public void Ping() => ObjectDisposedException.ThrowIf(_disposed, this);

        public void Dispose()
        {
            _disposed = true;
            Interlocked.Increment(ref DisposeCount);
        }
    }

    [Fact]
    public async Task RetiredCts_DoesNotAccumulateForSyncOnlyViewModel()
    {
        // For entries with only synchronous (or no) lifecycle callbacks,
        // ExitInFlightLifecycle is never called. BeginNewActivation must
        // dispose previously-retired CTSs that have been cancelled, otherwise
        // _retiredCts grows unboundedly on repeated activations.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(
            retention: NavigationRetention.KeepAlive);
        Assert.True(result);

        // Repeated refreshes each retire the previous CTS.
        for (int i = 0; i < 10; i++)
        {
            var refreshResult = await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(
                refreshIfActive: true);
            Assert.True(refreshResult);
        }

        int retiredCount = GetRetiredCtsCount(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.True(retiredCount <= 2,
            $"_retiredCts should stay bounded, but has {retiredCount} entries after 10 refreshes");
    }

    [Fact]
    public async Task BeginNewActivation_DoesNotDisposeUncancelledCts()
    {
        // A concurrent activation must not dispose a CTS before its owning
        // thread has cancelled it — that would lose the cancel signal via
        // ObjectDisposedException in CancelOutsideLocks.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>();
        Assert.True(result);

        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);

        var entryType = entry.GetType();
        var beginMethod = entryType.GetMethod("BeginNewActivation",
            BindingFlags.Public | BindingFlags.Instance)!;

        // First activation retires the initial CTS (not yet cancelled).
        var cts1 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.False(cts1.IsCancellationRequested);
        Assert.False(IsCtsDisposed(cts1));

        // Second activation must NOT dispose cts1: it hasn't been cancelled yet.
        var cts2 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.False(IsCtsDisposed(cts1),
            "BeginNewActivation disposed a CTS before its owner cancelled it");

        // After the owner cancels cts1, the next activation may dispose it.
        NavigationEntry_CancelRetiredOutsideLocks(entry!, cts1);
        Assert.True(cts1.IsCancellationRequested);

        var cts3 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.True(IsCtsDisposed(cts1),
            "BeginNewActivation should dispose a previously-cancelled retired CTS");
        Assert.False(IsCtsDisposed(cts2));

        // Cleanup.
        NavigationEntry_CancelRetiredOutsideLocks(entry!, cts2);
        NavigationEntry_CancelRetiredOutsideLocks(entry!, cts3);
    }

    [Fact]
    public async Task ExitInFlightLifecycle_DoesNotDisposeUncancelledCts()
    {
        // ExitInFlightLifecycle must apply the same settled-cancellation
        // gate as BeginNewActivation: when the last in-flight callback exits,
        // it must not dispose a CTS whose owner has not yet cancelled it.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>();
        Assert.True(result);

        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);

        var entryType = entry.GetType();
        var beginMethod = entryType.GetMethod("BeginNewActivation",
            BindingFlags.Public | BindingFlags.Instance)!;
        var enterMethod = entryType.GetMethod("EnterInFlightLifecycle",
            BindingFlags.Public | BindingFlags.Instance)!;
        var exitMethod = entryType.GetMethod("ExitInFlightLifecycle",
            BindingFlags.Public | BindingFlags.Instance)!;

        // Simulate one in-flight async callback.
        var gateTask = (Task)enterMethod.Invoke(entry, null)!;

        // New activation retires a CTS while a callback is in flight.
        // BeginNewActivation must not clean up (in-flight count > 0).
        var cts1 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.False(cts1.IsCancellationRequested);

        // The callback exits, dropping the count to zero. ExitInFlightLifecycle
        // must NOT dispose cts1: its owner hasn't cancelled it yet.
        exitMethod.Invoke(entry, new object[] { gateTask });
        Assert.False(IsCtsDisposed(cts1),
            "ExitInFlightLifecycle disposed a CTS before its owner cancelled it");

        // After the owner cancels, a later activation reclaims it.
        NavigationEntry_CancelRetiredOutsideLocks(entry!, cts1);
        var cts2 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.True(IsCtsDisposed(cts1));

        NavigationEntry_CancelRetiredOutsideLocks(entry!, cts2);
    }

    [Fact]
    public async Task RetiredCts_IsNotDisposedWhileItsCancelIsStillRunningCallbacks()
    {
        // IsCancellationRequested turns true the moment Cancel() begins,
        // while user callbacks are still executing inside it. A reclaim pass
        // on another thread must not dispose the source in that window —
        // disposal is gated on the cancellation having settled (Cancel()
        // returned), not on it having been requested.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        var result = await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>();
        Assert.True(result);

        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);

        var entryType = entry.GetType();
        var beginMethod = entryType.GetMethod("BeginNewActivation",
            BindingFlags.Public | BindingFlags.Instance)!;
        var cancelRetiredMethod = entryType.GetMethod("CancelRetiredOutsideLocks",
            BindingFlags.Public | BindingFlags.Instance)!;

        // Retire the initial CTS; its owner cancels it below.
        var cts1 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;

        // A user callback that blocks inside Cancel() and then uses its
        // token: disposing the source mid-callback surfaces as an
        // ObjectDisposedException here.
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cts1.Token.Register(() =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Task.GetAwaiter().GetResult();
            try
            {
                using var registration = cts1.Token.Register(static () => { });
                callbackFinished.TrySetResult(null);
            }
            catch (Exception ex)
            {
                callbackFinished.TrySetResult(ex);
            }
        });

        // The owner cancels on a background thread; Cancel() blocks in the
        // callback above.
        var cancelTask = Task.Run(() => cancelRetiredMethod.Invoke(entry, new object[] { cts1 }));
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(cts1.IsCancellationRequested);

        // While the cancel is in flight, another activation runs its reclaim
        // pass: cts1 is requested but not settled, so it must survive.
        var cts2 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.False(IsCtsDisposed(cts1),
            "Disposed a retired CTS while its Cancel() was still running callbacks");

        releaseCallback.TrySetResult();
        await cancelTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(await callbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        // Settled now: the next activation reclaims cts1.
        var cts3 = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.True(IsCtsDisposed(cts1));

        // Cleanup.
        NavigationEntry_CancelRetiredOutsideLocks(entry, cts2);
        NavigationEntry_CancelRetiredOutsideLocks(entry, cts3);
    }

    [Fact]
    public async Task PopToExisting_ReactivatedEntry_SettlesAndReclaimsRetiredCts()
    {
        // The pop-to-existing path reactivates the buried entry through
        // BeginNewActivation and cancels the retired source through the
        // settled path: the source must be reclaimable by the next
        // activation instead of accumulating in the retired list.
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        Assert.True(await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(
            retention: NavigationRetention.KeepAlive));
        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);
        var lifecycleProperty = entry.GetType().GetProperty("LifecycleCts",
            BindingFlags.Public | BindingFlags.Instance)!;
        var cts1 = (CancellationTokenSource)lifecycleProperty.GetValue(entry)!;

        // Cover the page, then navigate back to its type: pop-to-existing
        // reactivates the buried entry and retires cts1.
        Assert.True(await nav.NavigateToAsync<TestHomeViewModel>());
        Assert.True(await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(
            retention: NavigationRetention.KeepAlive));
        Assert.True(cts1.IsCancellationRequested);

        // The next activation (a refresh) reclaims the settled cts1.
        Assert.True(await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(
            refreshIfActive: true));
        Assert.True(IsCtsDisposed(cts1),
            "Pop-to-existing retired CTS was not reclaimed after settling");
    }

    [Fact]
    public async Task ClearCache_DestroyedCacheEntry_LifecycleCtsIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        await nav.GoBackAsync();

        // The popped KeepAlive entry now sits in the cache. Clearing the
        // cache destroys it: its lifecycle CTS must be disposed with it,
        // not merely cancelled and left for the GC.
        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);
        var cts = (CancellationTokenSource)entry.GetType().GetProperty("LifecycleCts",
            BindingFlags.Public | BindingFlags.Instance)!.GetValue(entry)!;
        Assert.False(IsCtsDisposed(cts));

        nav.ClearCache("MainRegion");

        Assert.True(cts.IsCancellationRequested);
        Assert.True(IsCtsDisposed(cts),
            "ClearCache left the destroyed entry's lifecycle CTS undisposed");
    }

    [Fact]
    public async Task ClearCacheAsync_DestroyedCacheEntry_LifecycleCtsIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        await nav.GoBackAsync();

        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);
        var cts = (CancellationTokenSource)entry.GetType().GetProperty("LifecycleCts",
            BindingFlags.Public | BindingFlags.Instance)!.GetValue(entry)!;
        Assert.False(IsCtsDisposed(cts));

        await nav.ClearCacheAsync("MainRegion");

        Assert.True(cts.IsCancellationRequested);
        Assert.True(IsCtsDisposed(cts),
            "ClearCacheAsync left the destroyed entry's lifecycle CTS undisposed");
    }

    [Fact]
    public async Task ClearAllCache_DestroyedCacheEntry_LifecycleCtsIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        await nav.GoBackAsync();

        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);
        var cts = (CancellationTokenSource)entry.GetType().GetProperty("LifecycleCts",
            BindingFlags.Public | BindingFlags.Instance)!.GetValue(entry)!;
        Assert.False(IsCtsDisposed(cts));

        nav.ClearAllCache();

        Assert.True(cts.IsCancellationRequested);
        Assert.True(IsCtsDisposed(cts),
            "ClearAllCache left the destroyed entry's lifecycle CTS undisposed");
    }

    [Fact]
    public async Task ClearAllCacheAsync_DestroyedCacheEntry_LifecycleCtsIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        await nav.GoBackAsync();

        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);
        var cts = (CancellationTokenSource)entry.GetType().GetProperty("LifecycleCts",
            BindingFlags.Public | BindingFlags.Instance)!.GetValue(entry)!;
        Assert.False(IsCtsDisposed(cts));

        await nav.ClearAllCacheAsync();

        Assert.True(cts.IsCancellationRequested);
        Assert.True(IsCtsDisposed(cts),
            "ClearAllCacheAsync left the destroyed entry's lifecycle CTS undisposed");
    }

    [Fact]
    public async Task Dispose_EntryWithSettledRetiredCts_BothSourcesDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<SyncOnlyKeepAliveViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<SyncOnlyKeepAliveViewModel>(retention: NavigationRetention.KeepAlive);
        var entry = GetNavigationEntry(nav, "MainRegion", typeof(SyncOnlyKeepAliveViewModel));
        Assert.NotNull(entry);
        var entryType = entry.GetType();
        var lifecycleProperty = entryType.GetProperty("LifecycleCts",
            BindingFlags.Public | BindingFlags.Instance)!;
        var cts1 = (CancellationTokenSource)lifecycleProperty.GetValue(entry)!;

        // Retire cts1 and settle its cancellation directly on the entry,
        // with no subsequent activation or callback exit to reclaim it —
        // the state a destroyed entry can carry into a cache clear or a
        // service disposal. (Through the public navigation flow the exit
        // sweep normally reclaims a settled source first, which is why
        // this state is constructed on the entry itself.)
        var beginMethod = entryType.GetMethod("BeginNewActivation",
            BindingFlags.Public | BindingFlags.Instance)!;
        var retired = (CancellationTokenSource)beginMethod.Invoke(entry, null)!;
        Assert.Same(cts1, retired);
        NavigationEntry_CancelRetiredOutsideLocks(entry, retired);
        Assert.True(cts1.IsCancellationRequested);
        Assert.False(IsCtsDisposed(cts1));
        var cts2 = (CancellationTokenSource)lifecycleProperty.GetValue(entry)!;
        Assert.NotSame(cts1, cts2);

        nav.Dispose();

        Assert.True(IsCtsDisposed(cts2),
            "Dispose left the entry's current lifecycle CTS undisposed");
        Assert.True(IsCtsDisposed(cts1),
            "Dispose left the entry's settled retired CTS undisposed");
    }

    [Fact]
    public async Task Dispose_DestroyedEntry_LifecycleCtsIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        var cts = GetActiveEntryCts(nav, "MainRegion");
        Assert.NotNull(cts);
        Assert.False(IsCtsDisposed(cts));

        nav.Dispose();

        Assert.True(IsCtsDisposed(cts),
            "Dispose left the destroyed entry's lifecycle CTS undisposed");
    }

    [Fact]
    public async Task DisposeAsync_DestroyedEntry_LifecycleCtsIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        var provider = services.BuildServiceProvider();
        var nav = (NavigationService)provider.GetRequiredService<INavigationService>();

        await nav.NavigateToAsync<TestHomeViewModel>();
        var cts = GetActiveEntryCts(nav, "MainRegion");
        Assert.NotNull(cts);
        Assert.False(IsCtsDisposed(cts));

        await nav.DisposeAsync();

        Assert.True(IsCtsDisposed(cts),
            "DisposeAsync left the destroyed entry's lifecycle CTS undisposed");
    }

    private static bool WindowsContain(NavigationService nav, string regionName)
    {
        var field = typeof(NavigationService).GetField("_windows",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var windows = (System.Collections.IDictionary)field.GetValue(nav)!;
        return windows.Contains(regionName);
    }

    private static int GetRetiredCtsCount(
        NavigationService nav, string regionName, Type viewModelType)
    {
        var entry = GetNavigationEntry(nav, regionName, viewModelType);
        Assert.NotNull(entry);
        var field = entry.GetType().GetField("_retiredCts",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (System.Collections.IList)field.GetValue(entry)!;
        return list.Count;
    }

    private static object? GetNavigationEntry(
        NavigationService nav, string regionName, Type viewModelType)
    {
        var navType = typeof(NavigationService);

        // 1. Check _regionStacks: Dictionary<string, Stack<NavigationEntry>>
        var stacksField = navType.GetField("_regionStacks",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (stacksField?.GetValue(nav) is System.Collections.IDictionary stacksDict &&
            stacksDict[regionName] is System.Collections.IEnumerable stack)
        {
            foreach (var entry in stack)
            {
                if (IsEntryForViewModel(entry, viewModelType))
                    return entry;
            }
        }

        // 2. Check _keepAliveCache: Dictionary<(string, Type), NavigationEntry>
        var cacheField = navType.GetField("_keepAliveCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (cacheField?.GetValue(nav) is System.Collections.IDictionary cacheDict)
        {
            foreach (System.Collections.DictionaryEntry kv in cacheDict)
            {
                if (kv.Value is not null && IsEntryForViewModel(kv.Value, viewModelType))
                    return kv.Value;
            }
        }

        return null;
    }

    private static bool IsEntryForViewModel(object entry, Type viewModelType)
    {
        var vmProp = entry.GetType().GetProperty("ViewModel");
        var vm = vmProp?.GetValue(entry);
        return vm is not null && vm.GetType() == viewModelType;
    }

    private static bool IsCtsDisposed(CancellationTokenSource cts)
    {
        try
        {
            _ = cts.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static void NavigationEntry_CancelRetiredOutsideLocks(object entry, CancellationTokenSource cts)
    {
        var method = entry.GetType().GetMethod("CancelRetiredOutsideLocks",
            BindingFlags.Public | BindingFlags.Instance)!;
        method.Invoke(entry, new object[] { cts });
    }

    private sealed class SyncOnlyKeepAliveViewModel : NavigationViewModelBase, INavigationAware
    {
        public void OnNavigatedTo(object? parameter) { }
        public void OnNavigatedFrom() { }
    }

    #endregion
}

