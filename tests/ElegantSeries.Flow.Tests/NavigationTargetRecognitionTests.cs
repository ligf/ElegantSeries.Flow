using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>Value-equal navigation parameter for the recognition tests.</summary>
public record OrderRef(int Id);

/// <summary>Value-equal document parameter.</summary>
public record DocumentRef(string Path);

/// <summary>
/// A page parameter without value equality: a plain reference type that does
/// not override <see cref="object.Equals(object)"/>.
/// </summary>
public class PlainOrderDto
{
    public PlainOrderDto(int id) => Id = id;

    public int Id { get; }
}

/// <summary>
/// A KeepAlive detail page that relies purely on parameter comparison for
/// recognition (it does not implement <see cref="INavigationTargetAware"/>).
/// </summary>
public sealed class RecordParamDetailViewModel : NavigationViewModelBase, INavigationAware, IDisposable
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
/// A document page whose identity is its <i>current</i> file path: "Save As"
/// changes who the instance is after it was opened with the old path.
/// </summary>
public sealed class VotingDocumentViewModel : NavigationViewModelBase, INavigationAware, INavigationTargetAware
{
    public string FilePath { get; private set; } = "";
    public object? LastParameter { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        LastParameter = parameter;
        FilePath = ((DocumentRef)parameter!).Path;
    }

    public void OnNavigatedFrom()
    {
    }

    public void SaveAs(string newPath) => FilePath = newPath;

    public bool IsNavigationTarget(object? parameter) =>
        parameter is DocumentRef doc && doc.Path == FilePath;
}

/// <summary>A page that never claims an incoming navigation, whatever the parameter.</summary>
public sealed class AlwaysNoVotingViewModel : NavigationViewModelBase, INavigationAware, INavigationTargetAware
{
    public object? LastParameter { get; private set; }

    public void OnNavigatedTo(object? parameter) => LastParameter = parameter;

    public void OnNavigatedFrom()
    {
    }

    public bool IsNavigationTarget(object? parameter) => false;
}

/// <summary>
/// A page whose parameters have no value equality, used with
/// <see cref="PlainOrderDto"/>.
/// </summary>
public sealed class DtoParamViewModel : NavigationViewModelBase, INavigationAware
{
    public object? LastParameter { get; private set; }

    public void OnNavigatedTo(object? parameter) => LastParameter = parameter;

    public void OnNavigatedFrom()
    {
    }
}

/// <summary>A voting page whose vote always throws.</summary>
public sealed class ThrowingVotingViewModel : NavigationViewModelBase, INavigationAware, INavigationTargetAware
{
    public void OnNavigatedTo(object? parameter)
    {
    }

    public void OnNavigatedFrom()
    {
    }

    public bool IsNavigationTarget(object? parameter) =>
        throw new InvalidOperationException("Vote failed.");
}

/// <summary>A parameter type whose <see cref="object.Equals(object)"/> always throws.</summary>
public class ThrowingEqualsDto
{
    public override bool Equals(object? obj) =>
        throw new InvalidOperationException("Equals failed.");

    public override int GetHashCode() => 0;
}

/// <summary>
/// A voting page that inspects the navigation service through a read-only
/// query while being classified — classification must not hold the state
/// lock, or this deadlocks. (A covered page's <c>Navigation</c> reference is
/// detached, so the service is captured while the page is active.)
/// </summary>
public sealed class SelfInspectingVotingViewModel : NavigationViewModelBase, INavigationAware, INavigationTargetAware
{
    private INavigationService? _service;

    public object? LastParameter { get; private set; }
    public int LastObservedStackCount { get; private set; } = -1;

    public void OnNavigatedTo(object? parameter)
    {
        LastParameter = parameter;
        _service = Navigation;
    }

    public void OnNavigatedFrom()
    {
    }

    public bool IsNavigationTarget(object? parameter)
    {
        LastObservedStackCount = _service?.GetNavigationStack("MainRegion").Count ?? -1;
        return object.Equals(LastParameter, parameter);
    }
}

public class NavigationTargetRecognitionTests
{
    private readonly INavigationService _navigationService;

    public NavigationTargetRecognitionTests()
    {
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddTransient<TestHomeViewModel>();
        services.AddTransient<RecordParamDetailViewModel>();
        services.AddTransient<VotingDocumentViewModel>();
        services.AddTransient<AlwaysNoVotingViewModel>();
        services.AddTransient<DtoParamViewModel>();
        services.AddTransient<ThrowingVotingViewModel>();
        services.AddTransient<SelfInspectingVotingViewModel>();
        services.AddSingleton<SingletonVotingDocumentViewModel>();
        services.AddSingleton<SingletonDtoPageViewModel>();

        var provider = services.BuildServiceProvider();
        _navigationService = provider.GetRequiredService<INavigationService>();
    }

    /// <summary>A singleton page taking a parameter without value equality.</summary>
    public sealed class SingletonDtoPageViewModel : NavigationViewModelBase, INavigationAware
    {
        public object? LastParameter { get; private set; }

        public void OnNavigatedTo(object? parameter) => LastParameter = parameter;

        public void OnNavigatedFrom()
        {
        }
    }

    /// <summary>The same document page, registered as a singleton.</summary>
    public sealed class SingletonVotingDocumentViewModel : NavigationViewModelBase, INavigationAware, INavigationTargetAware
    {
        public string FilePath { get; private set; } = "";

        public void OnNavigatedTo(object? parameter) => FilePath = ((DocumentRef)parameter!).Path;

        public void OnNavigatedFrom()
        {
        }

        public void SaveAs(string newPath) => FilePath = newPath;

        public bool IsNavigationTarget(object? parameter) =>
            parameter is DocumentRef doc && doc.Path == FilePath;
    }

    [Fact]
    public async Task RecordParameter_TwoInstancesCoexist_AndRecognitionFindsTheRightOne()
    {
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), retention: NavigationRetention.KeepAlive);
        var order42 = (RecordParamDetailViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // A different parameter addresses a different page: a second instance
        // is created instead of re-purposing the first one with 43.
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(43), retention: NavigationRetention.KeepAlive);
        var order43 = (RecordParamDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(order42, order43);
        Assert.Equal(new OrderRef(42), order42.LastParameter);

        // Re-navigating to 42 recognizes the original instance under the
        // stack and pops back to it.
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), retention: NavigationRetention.KeepAlive);
        Assert.Same(order42, _navigationService.GetCurrentViewModel());

        // The 43 instance went to the KeepAlive cache when it was popped, and
        // is recognized there on the next navigation.
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(43), retention: NavigationRetention.KeepAlive);
        Assert.Same(order43, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Cache_RecognizedParameter_ReusesCachedInstance()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), retention: NavigationRetention.KeepAlive);
        var vm = (RecordParamDetailViewModel)_navigationService.GetCurrentViewModel()!;

        // Pop the page into the KeepAlive cache.
        Assert.True(await _navigationService.GoBackAsync());

        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), retention: NavigationRetention.KeepAlive);
        Assert.Same(vm, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Cache_UnrecognizedParameter_BypassesCachedInstance_AndEvictsItOnNextCacheWrite()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), retention: NavigationRetention.KeepAlive);
        var first = (RecordParamDetailViewModel)_navigationService.GetCurrentViewModel()!;

        // Pop the page into the KeepAlive cache.
        Assert.True(await _navigationService.GoBackAsync());

        // The cached 42 instance is not the target of a 43 navigation: it is
        // bypassed (still cached, still alive) and a fresh instance is used.
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(43), retention: NavigationRetention.KeepAlive);
        var second = (RecordParamDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.False(first.Disposed);

        // When the new instance is popped into the cache, the bypassed entry
        // is evicted through the standard plan and its scope is disposed.
        Assert.True(await _navigationService.GoBackAsync());
        Assert.True(first.Disposed);
        Assert.False(second.Disposed);

        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(43), retention: NavigationRetention.KeepAlive);
        Assert.Same(second, _navigationService.GetCurrentViewModel());
    }

    [Fact]
    public async Task Vote_OverridesParameterEquality_WhenItSaysNo()
    {
        await _navigationService.NavigateToAsync<AlwaysNoVotingViewModel, string>(
            "x", retention: NavigationRetention.KeepAlive);
        var first = (AlwaysNoVotingViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // The stored parameter equals the incoming one, so parameter
        // comparison alone would reuse the instance — but the instance's own
        // verdict is final and it says no.
        await _navigationService.NavigateToAsync<AlwaysNoVotingViewModel, string>(
            "x", retention: NavigationRetention.KeepAlive);
        var second = (AlwaysNoVotingViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Vote_ClaimsByCurrentState_NotByStoredParameter()
    {
        await _navigationService.NavigateToAsync<VotingDocumentViewModel, DocumentRef>(
            new DocumentRef("a.txt"), retention: NavigationRetention.KeepAlive);
        var doc = (VotingDocumentViewModel)_navigationService.GetCurrentViewModel()!;

        // The document is saved under a new name: the instance's identity is
        // now b.txt, while its stored navigation parameter still says a.txt.
        doc.SaveAs("b.txt");

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // Parameter comparison against the stored parameter could never match
        // b.txt; the instance claims the navigation by its current state.
        await _navigationService.NavigateToAsync<VotingDocumentViewModel, DocumentRef>(
            new DocumentRef("b.txt"), retention: NavigationRetention.KeepAlive);
        Assert.Same(doc, _navigationService.GetCurrentViewModel());
        Assert.Equal(new DocumentRef("b.txt"), doc.LastParameter);
    }

    [Fact]
    public async Task Vote_CanCallReadOnlyServiceQueries_WithoutDeadlock()
    {
        await _navigationService.NavigateToAsync<SelfInspectingVotingViewModel, string>(
            "x", retention: NavigationRetention.KeepAlive);
        var vm = (SelfInspectingVotingViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        await _navigationService.NavigateToAsync<SelfInspectingVotingViewModel, string>(
            "x", retention: NavigationRetention.KeepAlive);

        Assert.Same(vm, _navigationService.GetCurrentViewModel());
        Assert.True(vm.LastObservedStackCount >= 1);
    }

    [Fact]
    public async Task PlainDtoParameter_NeverRecognized_AndReportsDiagnosticOncePerType()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += errors.Add;

        await _navigationService.NavigateToAsync<DtoParamViewModel, PlainOrderDto>(
            new PlainOrderDto(7), retention: NavigationRetention.KeepAlive);
        var first = (DtoParamViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // Even an identical Id does not match: the parameter type has no
        // value equality, so a new instance is created on every navigation.
        await _navigationService.NavigateToAsync<DtoParamViewModel, PlainOrderDto>(
            new PlainOrderDto(7), retention: NavigationRetention.KeepAlive);
        var second = (DtoParamViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);

        var diagnostic = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(diagnostic);
        Assert.Contains(nameof(PlainOrderDto), diagnostic.Message);
        Assert.Contains("value equality", diagnostic.Message);

        // A further navigation with the same parameter type does not report again.
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<DtoParamViewModel, PlainOrderDto>(
            new PlainOrderDto(9), retention: NavigationRetention.KeepAlive);
        Assert.Single(errors);
    }

    [Fact]
    public async Task VoteRejection_DoesNotTriggerUnmatchableDiagnostic()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += errors.Add;

        await _navigationService.NavigateToAsync<AlwaysNoVotingViewModel, PlainOrderDto>(
            new PlainOrderDto(7), retention: NavigationRetention.KeepAlive);
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // The candidate was rejected by its own vote, not by parameter
        // comparison — the unmatchable-parameter diagnostic must not fire.
        await _navigationService.NavigateToAsync<AlwaysNoVotingViewModel, PlainOrderDto>(
            new PlainOrderDto(7), retention: NavigationRetention.KeepAlive);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task RecordParameter_NonMatching_DoesNotReportDiagnostic()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += errors.Add;

        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), retention: NavigationRetention.KeepAlive);
        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // 43 legitimately differs from 42: the parameter type has value
        // equality, so this rejection is by value, not by unmatchability.
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(43), retention: NavigationRetention.KeepAlive);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task NearestMatchingCandidate_Wins()
    {
        await _navigationService.NavigateToAsync<VotingDocumentViewModel, DocumentRef>(
            new DocumentRef("a.txt"), retention: NavigationRetention.KeepAlive);
        var lower = (VotingDocumentViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // The lower instance now represents b.txt, so an a.txt navigation
        // creates a second instance above it.
        lower.SaveAs("b.txt");
        await _navigationService.NavigateToAsync<VotingDocumentViewModel, DocumentRef>(
            new DocumentRef("a.txt"), retention: NavigationRetention.KeepAlive);
        var upper = (VotingDocumentViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(lower, upper);

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // Both instances would now claim an a.txt navigation; the one
        // nearest to the stack top must win, leaving the lower one in place.
        lower.SaveAs("a.txt");
        await _navigationService.NavigateToAsync<VotingDocumentViewModel, DocumentRef>(
            new DocumentRef("a.txt"), retention: NavigationRetention.KeepAlive);

        Assert.Same(upper, _navigationService.GetCurrentViewModel());
        Assert.Equal(3, _navigationService.GetNavigationStack("MainRegion").Count);
    }

    [Fact]
    public async Task ThrowingVote_IsReported_AndANewInstanceIsCreated()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += errors.Add;

        await _navigationService.NavigateToAsync<ThrowingVotingViewModel, string>(
            "x", retention: NavigationRetention.KeepAlive);
        var first = (ThrowingVotingViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        // The candidate's vote throws: the failure is reported through
        // SubscriberError, the candidate counts as not recognized, and the
        // navigation still succeeds with a new instance.
        var result = await _navigationService.NavigateToAsync<ThrowingVotingViewModel, string>(
            "x", retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        Assert.NotSame(first, _navigationService.GetCurrentViewModel());
        var error = Assert.Single(errors);
        Assert.Equal("Vote failed.", error.Message);
    }

    [Fact]
    public async Task ThrowingParameterEquals_IsReported_AndANewInstanceIsCreated()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += errors.Add;

        await _navigationService.NavigateToAsync<DtoParamViewModel, ThrowingEqualsDto>(
            new ThrowingEqualsDto(), retention: NavigationRetention.KeepAlive);
        var first = (DtoParamViewModel)_navigationService.GetCurrentViewModel()!;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();

        var result = await _navigationService.NavigateToAsync<DtoParamViewModel, ThrowingEqualsDto>(
            new ThrowingEqualsDto(), retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        Assert.NotSame(first, _navigationService.GetCurrentViewModel());
        var error = Assert.Single(errors);
        Assert.Equal("Equals failed.", error.Message);
    }

    [Fact]
    public async Task Reset_UnrecognizedCachedInstance_IsBypassed_RecognizedOneIsAdopted()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), retention: NavigationRetention.KeepAlive);
        var first = (RecordParamDetailViewModel)_navigationService.GetCurrentViewModel()!;

        // Pop the page into the KeepAlive cache.
        Assert.True(await _navigationService.GoBackAsync());

        // The recognition gate applies to explicit operations too: a Reset
        // addressed to 43 does not adopt and refresh the cached 42 instance.
        var reset = await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(43), operation: NavigationOperation.Reset, retention: NavigationRetention.KeepAlive);

        Assert.True(reset);
        var second = (RecordParamDetailViewModel)_navigationService.GetCurrentViewModel()!;
        Assert.NotSame(first, second);
        Assert.Single(_navigationService.GetNavigationStack("MainRegion"));
        Assert.False(first.Disposed);

        // A Reset addressed to 42 recognizes and adopts the cached instance.
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        var resetBack = await _navigationService.NavigateToAsync<RecordParamDetailViewModel, OrderRef>(
            new OrderRef(42), operation: NavigationOperation.Reset, retention: NavigationRetention.KeepAlive);

        Assert.True(resetBack);
        Assert.Same(first, _navigationService.GetCurrentViewModel());
        Assert.Single(_navigationService.GetNavigationStack("MainRegion"));
    }

    [Fact]
    public async Task Singleton_UnmatchableParameter_AdoptedByIdentity_DoesNotReportDiagnostic()
    {
        var errors = new List<Exception>();
        _navigationService.SubscriberError += errors.Add;

        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<SingletonDtoPageViewModel, PlainOrderDto>(
            new PlainOrderDto(7), retention: NavigationRetention.KeepAlive);
        var page = (SingletonDtoPageViewModel)_navigationService.GetCurrentViewModel()!;

        // Pop the singleton page into the KeepAlive cache.
        Assert.True(await _navigationService.GoBackAsync());

        // The cache gate cannot recognize the new parameter (its type has
        // no value equality), but resolving the page returns the same
        // singleton and the commit adopts its cached entry by identity —
        // the instance IS reused, so the unmatchable-parameter diagnostic
        // must not fire, nor consume the once-per-type report.
        var result = await _navigationService.NavigateToAsync<SingletonDtoPageViewModel, PlainOrderDto>(
            new PlainOrderDto(8), retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        Assert.Same(page, _navigationService.GetCurrentViewModel());
        Assert.Empty(errors);

        // The report slot is intact: a genuinely unreused navigation with
        // the same parameter type still reports, exactly once.
        await _navigationService.NavigateToAsync<DtoParamViewModel, PlainOrderDto>(
            new PlainOrderDto(7), retention: NavigationRetention.KeepAlive);
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<DtoParamViewModel, PlainOrderDto>(
            new PlainOrderDto(7), retention: NavigationRetention.KeepAlive);
        Assert.Single(errors);
    }

    [Fact]
    public async Task Singleton_VoteRejectsCachedInstance_NavigationStillLandsOnTheSingleton()
    {
        await _navigationService.NavigateToAsync<TestHomeViewModel>();
        await _navigationService.NavigateToAsync<SingletonVotingDocumentViewModel, DocumentRef>(
            new DocumentRef("a.txt"), retention: NavigationRetention.KeepAlive);
        var doc = (SingletonVotingDocumentViewModel)_navigationService.GetCurrentViewModel()!;

        // Pop the singleton page into the KeepAlive cache.
        Assert.True(await _navigationService.GoBackAsync());
        doc.SaveAs("b.txt");

        // The cached singleton votes no for a.txt (it now represents b.txt),
        // so the cache entry is bypassed — but resolving the page yields the
        // same singleton instance, and instance identity lands the navigation
        // on it without ever pushing a duplicate.
        var result = await _navigationService.NavigateToAsync<SingletonVotingDocumentViewModel, DocumentRef>(
            new DocumentRef("a.txt"), retention: NavigationRetention.KeepAlive);

        Assert.True(result);
        Assert.Same(doc, _navigationService.GetCurrentViewModel());
        Assert.Equal(2, _navigationService.GetNavigationStack("MainRegion").Count);
    }
}
