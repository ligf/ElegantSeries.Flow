using Microsoft.Extensions.DependencyInjection;
using ElegantSeries.Flow.Extensions;
using ElegantSeries.Flow.Navigation;
using ElegantSeries.Flow.ViewModels;

namespace ElegantSeries.Flow.Tests;

/// <summary>
/// Model-based random walk: a sequential driver issues awaited, external
/// navigation operations against two regions while a mirror model predicts
/// every observable consequence — the fate result, the stack (instance
/// identity and stored parameter per entry), the current page and its
/// retention, <c>CanGoBack</c>, per-instance lifecycle counts, the set of
/// disposed instances, instance creation counts (including the throwaway
/// resolution a recognized navigation discards), and region-navigated event
/// counts. After every step the full actual state is compared against the
/// model; a mismatch fails with the seed, step, operation history and both
/// states.
/// </summary>
/// <remarks>
/// Deliberately out of scope (covered by dedicated tests): concurrency,
/// window-model supersession, callback-registered navigation, chain limits,
/// cancellation, target-aware votes, and parameters without value equality.
/// Pages are synchronous lifecycle pages with a value-equal parameter
/// record whose tiny domain (ids 0–2, plus a guard-veto sentinel) forces
/// constant recognition collisions. Set the environment variable
/// FLOW_MODEL_STEPS to raise the per-seed step count (e.g. 10000 for a
/// nightly run); the default is 1000 steps for each of three fixed seeds.
/// </remarks>
public class ModelWalkTests
{
    private const string RegionA = "MainRegion";
    private const string RegionB = "Second";
    private static readonly string[] Regions = [RegionA, RegionB];

    [Fact]
    public async Task ModelWalk_Seed20261024() => await RunWalkAsync(20261024);

    [Fact]
    public async Task ModelWalk_Seed777() => await RunWalkAsync(777);

    [Fact]
    public async Task ModelWalk_Seed31337() => await RunWalkAsync(31337);

    private static int StepsPerSeed =>
        int.TryParse(Environment.GetEnvironmentVariable("FLOW_MODEL_STEPS"), out var steps) && steps > 0
            ? steps
            : 1000;

    // ── Driver ────────────────────────────────────────────────

    private async Task RunWalkAsync(int seed)
    {
        var ledger = new ModelLedger();
        var services = new ServiceCollection();
        services.AddElegantSeriesFlow();
        services.AddSingleton(ledger);
        services.AddTransient<ModelPageA>();
        services.AddTransient<ModelPageB>();
        services.AddTransient<ModelGuardedPage>();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<INavigationService>();

        var model = new Model();
        var eventCounts = Regions.ToDictionary(r => r, _ => 0);
        var lastEventInstance = Regions.ToDictionary(r => r, _ => -1);
        service.RegionNavigated += (region, vm) =>
        {
            eventCounts[region]++;
            lastEventInstance[region] = ((ModelPageBase)vm).InstanceId;
        };
        var cacheClearedCounts = Regions.ToDictionary(r => r, _ => 0);
        service.RegionCacheCleared += region => cacheClearedCounts[region]++;
        var releasedCounts = Regions.ToDictionary(r => r, _ => 0);
        service.ViewModelReleased += (region, _) => releasedCounts[region]++;
        var subscriberErrors = new List<Exception>();
        service.SubscriberError += subscriberErrors.Add;

        var rng = new Random(seed);
        var history = new List<string>();

        void Require(bool condition, string what)
        {
            if (!condition)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Model walk mismatch (seed {seed}, step {Math.Max(0, history.Count - 1)}): {what}\n" +
                    $"History (last 25):\n  {string.Join("\n  ", history.TakeLast(25))}\n" +
                    model.Dump(service, ledger));
            }
        }

        for (int step = 0; step < StepsPerSeed; step++)
        {
            var region = rng.Next(5) == 0 ? RegionB : RegionA;
            var m = model.Region(region);
            int roll = rng.Next(100);

            if (roll < 40 && m.Stack.Count <= 40)
            {
                // Push (40%)
                var (kind, param) = RandomTarget(rng);
                var retention = rng.Next(5) < 2 ? NavigationRetention.KeepAlive : NavigationRetention.Destroy;
                history.Add($"#{step} Push {kind}({param.Id}) [{retention}] -> {region}");
                int createdBefore = ledger.CreatedCount;
                var plan = model.PredictForward(m, kind, param, NavigationOperation.Push, retention, refresh: false);
                bool fate = await NavigateAsync(service, kind, param, region, NavigationOperation.Push, retention, refresh: false);
                Require(fate == plan.Fate, $"Push fate: model {plan.Fate}, actual {fate}.");
                model.ReconcileLanding(m, plan, service, ledger, createdBefore, Require);
            }
            else if (roll < 52)
            {
                // Replace (12%)
                var (kind, param) = RandomTarget(rng);
                var retention = rng.Next(5) < 2 ? NavigationRetention.KeepAlive : NavigationRetention.Destroy;
                history.Add($"#{step} Replace {kind}({param.Id}) [{retention}] -> {region}");
                int createdBefore = ledger.CreatedCount;
                var plan = model.PredictForward(m, kind, param, NavigationOperation.Replace, retention, refresh: false);
                bool fate = await NavigateAsync(service, kind, param, region, NavigationOperation.Replace, retention, refresh: false);
                Require(fate == plan.Fate, $"Replace fate: model {plan.Fate}, actual {fate}.");
                model.ReconcileLanding(m, plan, service, ledger, createdBefore, Require);
            }
            else if (roll < 60)
            {
                // Reset (8%)
                var (kind, param) = RandomTarget(rng);
                var retention = rng.Next(5) < 2 ? NavigationRetention.KeepAlive : NavigationRetention.Destroy;
                history.Add($"#{step} Reset {kind}({param.Id}) [{retention}] -> {region}");
                int createdBefore = ledger.CreatedCount;
                var plan = model.PredictForward(m, kind, param, NavigationOperation.Reset, retention, refresh: false);
                bool fate = await NavigateAsync(service, kind, param, region, NavigationOperation.Reset, retention, refresh: false);
                Require(fate == plan.Fate, $"Reset fate: model {plan.Fate}, actual {fate}.");
                model.ReconcileLanding(m, plan, service, ledger, createdBefore, Require);
            }
            else if (roll < 82)
            {
                // GoBack (22%)
                history.Add($"#{step} GoBack -> {region}");
                bool expected = model.PredictGoBack(m);
                bool fate = await service.GoBackAsync(region);
                Require(fate == expected, $"GoBack fate: model {expected}, actual {fate}.");
            }
            else if (roll < 88)
            {
                // ClearCache (6%), sync/async alternating
                bool sync = step % 2 == 0;
                history.Add($"#{step} ClearCache({(sync ? "sync" : "async")}) -> {region}");
                model.PredictClearCache(m);
                if (sync)
                {
                    service.ClearCache(region);
                }
                else
                {
                    await service.ClearCacheAsync(region);
                }
            }
            else if (roll < 91)
            {
                // ClearAllCache (3%), sync/async alternating
                bool sync = step % 2 == 0;
                history.Add($"#{step} ClearAllCache({(sync ? "sync" : "async")})");
                model.PredictClearAllCache();
                if (sync)
                {
                    service.ClearAllCache();
                }
                else
                {
                    await service.ClearAllCacheAsync();
                }
            }
            else
            {
                // Self-navigation (9%): Push+Destroy to the current page's own
                // type, with or without refresh; degrades to a plain Push on
                // an empty stack.
                var top = m.Stack.LastOrDefault();
                var kind = top?.Kind ?? RandomTarget(rng).Kind;
                var param = new P(rng.Next(3));
                bool refresh = rng.Next(2) == 0;
                history.Add($"#{step} SelfNav {kind}({param.Id}) refresh={refresh} -> {region}");
                int createdBefore = ledger.CreatedCount;
                var plan = model.PredictForward(m, kind, param, NavigationOperation.Push, NavigationRetention.Destroy, refresh);
                bool fate = await NavigateAsync(service, kind, param, region, NavigationOperation.Push, NavigationRetention.Destroy, refresh);
                Require(fate == plan.Fate, $"SelfNav fate: model {plan.Fate}, actual {fate}.");
                model.ReconcileLanding(m, plan, service, ledger, createdBefore, Require);
            }

            model.AssertState(service, ledger, eventCounts, lastEventInstance, cacheClearedCounts, releasedCounts, Require);
            Require(subscriberErrors.Count == 0,
                $"SubscriberError fired: {(subscriberErrors.Count > 0 ? subscriberErrors[0].GetType().Name + ": " + subscriberErrors[0].Message : "")}");
        }

        // Final balance: disposing the service disposes every remaining
        // stack and cache entry; nothing created may be left undisposed, and
        // every entry disposal (but not the discarded throwaway resolutions,
        // which never became entries) has raised ViewModelReleased once.
        await ((IAsyncDisposable)service).DisposeAsync();
        var created = ledger.CreatedSnapshot();
        var disposed = ledger.DisposedSnapshot();
        Require(disposed.Count == created.Count,
            $"Final ledger imbalance: created {created.Count}, disposed {disposed.Count}.");
        Require(releasedCounts.Values.Sum() == created.Count - model.ThrowawayCount,
            $"Final ViewModelReleased total: expected {created.Count - model.ThrowawayCount}, actual {releasedCounts.Values.Sum()}.");
    }

    private static (PageKind Kind, P Param) RandomTarget(Random rng)
    {
        var kind = (PageKind)rng.Next(3);
        // The guarded page occasionally carries the veto sentinel (-1); all
        // other parameters come from the collision domain 0..2.
        var param = kind == PageKind.Guarded && rng.Next(6) == 0 ? new P(-1) : new P(rng.Next(3));
        return (kind, param);
    }

    private static Task<bool> NavigateAsync(
        INavigationService service, PageKind kind, P param, string region,
        NavigationOperation operation, NavigationRetention retention, bool refresh) =>
        kind switch
        {
            PageKind.A => service.NavigateToAsync<ModelPageA, P>(param, region, operation, retention, refresh),
            PageKind.B => service.NavigateToAsync<ModelPageB, P>(param, region, operation, retention, refresh),
            _ => service.NavigateToAsync<ModelGuardedPage, P>(param, region, operation, retention, refresh),
        };

    // ── Model ─────────────────────────────────────────────────

    private enum PageKind { A, B, Guarded }

    private sealed class Entry
    {
        public required PageKind Kind;
        public required P Param;
        public required NavigationRetention Retention;
        public int Id; // instance id; -1 while a fresh landing is unreconciled

        /// <summary>Guarded pages only: the one-shot veto for the current activation has not fired yet.</summary>
        public bool VetoArmed;
    }

    private sealed class RegionModel
    {
        public required string Name;
        public List<Entry> Stack = []; // bottom → top
        public Dictionary<PageKind, Entry> Cache = [];
        public int EventCount;
        public int LastEventInstance = -1;
        public int CacheClearedCount;
        public int ReleasedCount;
    }

    private sealed class Plan
    {
        public bool Fate;
        public LandingKind Landing;
        public Entry? LandedEntry;      // for Fresh/Adopted/Recognized
        public bool ExpectThrowaway;    // a fresh resolution was discarded
    }

    private enum LandingKind { None, Fresh, Adopted, Recognized, Refreshed }

    private sealed class Model
    {
        private readonly Dictionary<string, RegionModel> _regions = new()
        {
            [RegionA] = new RegionModel { Name = RegionA },
            [RegionB] = new RegionModel { Name = RegionB },
        };

        private readonly Dictionary<int, (int To, int From)> _counts = [];
        private readonly HashSet<int> _disposed = [];

        /// <summary>Fresh resolutions discarded by a recognized landing (never entries, never released-events).</summary>
        public int ThrowawayCount { get; private set; }

        public RegionModel Region(string name) => _regions[name];

        /// <summary>
        /// Whether the entry's guard vetoes a departure right now: a guarded
        /// page activated with the sentinel parameter vetoes the first
        /// departure attempt of that activation (the attempt disarms it).
        /// </summary>
        private static bool IsVeto(Entry entry) =>
            entry.Kind == PageKind.Guarded && entry.Param.Id == -1 && entry.VetoArmed;

        private (int To, int From) Counts(int id) => _counts.GetValueOrDefault(id);

        private void BumpTo(Entry entry)
        {
            var c = Counts(entry.Id);
            _counts[entry.Id] = (c.To + 1, c.From);
        }

        private void BumpFrom(Entry entry)
        {
            var c = Counts(entry.Id);
            _counts[entry.Id] = (c.To, c.From + 1);
        }

        private void PopDisposeOrCache(RegionModel m, Entry entry)
        {
            if (entry.Retention == NavigationRetention.KeepAlive)
            {
                if (m.Cache.TryGetValue(entry.Kind, out var evicted))
                {
                    _disposed.Add(evicted.Id);
                    m.ReleasedCount++;
                }

                m.Cache[entry.Kind] = entry;
            }
            else
            {
                _disposed.Add(entry.Id);
                m.ReleasedCount++;
            }
        }

        /// <summary>
        /// Predicts a forward navigation and mutates the model. Fresh
        /// instance ids are reconciled after the call (see
        /// <see cref="ReconcileLanding"/>).
        /// </summary>
        public Plan PredictForward(
            RegionModel m, PageKind kind, P param,
            NavigationOperation operation, NavigationRetention retention, bool refresh)
        {
            var plan = new Plan { Fate = true, Landing = LandingKind.None };
            var top = m.Stack.LastOrDefault();

            // Self no-op: Push+Destroy to the active type without refresh
            // never starts a departure (parameter NOT updated).
            if (operation == NavigationOperation.Push
                && retention == NavigationRetention.Destroy
                && !refresh
                && top is not null && top.Kind == kind)
            {
                return plan;
            }

            // The departing page's guard vetoes everything else, refresh
            // included: fate false and no observable change (the attempt
            // disarms the one-shot veto).
            if (top is not null && IsVeto(top))
            {
                top.VetoArmed = false;
                plan.Fate = false;
                return plan;
            }

            // Self refresh: re-activation in place.
            if (operation == NavigationOperation.Push
                && retention == NavigationRetention.Destroy
                && refresh
                && top is not null && top.Kind == kind)
            {
                top.Param = param;
                top.VetoArmed = top.Kind == PageKind.Guarded && param.Id == -1;
                BumpTo(top);
                m.EventCount++;
                m.LastEventInstance = top.Id;
                plan.Landing = LandingKind.Refreshed;
                return plan;
            }

            // Cache consult (KeepAlive intents only): the cached entry must
            // claim the parameter; stack recognition below still outranks it.
            Entry? cacheClaim = null;
            if (retention == NavigationRetention.KeepAlive
                && m.Cache.TryGetValue(kind, out var cached)
                && Equals(cached.Param, param))
            {
                cacheClaim = cached;
            }

            // Stack recognition (Push only): KeepAlive entries of the same
            // type whose stored parameter equals the intent's, nearest first.
            Entry? stackClaim = null;
            if (operation == NavigationOperation.Push)
            {
                for (int i = m.Stack.Count - 1; i >= 0; i--)
                {
                    var e = m.Stack[i];
                    if (e.Kind == kind && e.Retention == NavigationRetention.KeepAlive && Equals(e.Param, param))
                    {
                        stackClaim = e;
                        break;
                    }
                }
            }

            // The departing page runs From exactly once in every landing path.
            if (top is not null)
            {
                BumpFrom(top);
            }

            if (stackClaim is not null)
            {
                // Pop everything above the claimed entry (covered pages run
                // no extra From); the entry keeps its retention, takes the
                // new parameter and is re-activated.
                while (!ReferenceEquals(m.Stack.Last(), stackClaim))
                {
                    var popped = m.Stack.Last();
                    m.Stack.RemoveAt(m.Stack.Count - 1);
                    PopDisposeOrCache(m, popped);
                }

                stackClaim.Param = param;
                stackClaim.VetoArmed = stackClaim.Kind == PageKind.Guarded && param.Id == -1;
                BumpTo(stackClaim);
                m.EventCount++;
                m.LastEventInstance = stackClaim.Id;
                plan.Landing = LandingKind.Recognized;
                plan.LandedEntry = stackClaim;
                // A fresh resolution happened (and is discarded) unless the
                // cache claim supplied the target instance.
                plan.ExpectThrowaway = cacheClaim is null;
                return plan;
            }

            Entry landed;
            if (cacheClaim is not null)
            {
                m.Cache.Remove(kind);
                landed = new Entry { Kind = kind, Param = param, Retention = retention, Id = cacheClaim.Id };
                plan.Landing = LandingKind.Adopted;
            }
            else
            {
                landed = new Entry { Kind = kind, Param = param, Retention = retention, Id = -1 };
                plan.Landing = LandingKind.Fresh;
            }

            switch (operation)
            {
                case NavigationOperation.Replace when m.Stack.Count > 0:
                    var old = m.Stack.Last();
                    m.Stack.RemoveAt(m.Stack.Count - 1);
                    PopDisposeOrCache(m, old);
                    break;
                case NavigationOperation.Reset:
                    while (m.Stack.Count > 0)
                    {
                        var e = m.Stack.Last();
                        m.Stack.RemoveAt(m.Stack.Count - 1);
                        PopDisposeOrCache(m, e);
                    }

                    break;
            }

            m.Stack.Add(landed);
            plan.LandedEntry = landed;
            if (plan.Landing == LandingKind.Adopted)
            {
                BumpTo(landed);
            }

            landed.VetoArmed = landed.Kind == PageKind.Guarded && param.Id == -1;
            m.EventCount++;
            m.LastEventInstance = landed.Id; // fixed up for Fresh in ReconcileLanding
            return plan;
        }

        public bool PredictGoBack(RegionModel m)
        {
            if (m.Stack.Count <= 1)
            {
                return false;
            }

            var top = m.Stack.Last();
            if (IsVeto(top))
            {
                top.VetoArmed = false;
                return false;
            }

            BumpFrom(top);
            m.Stack.RemoveAt(m.Stack.Count - 1);
            PopDisposeOrCache(m, top);
            var revealed = m.Stack.Last();
            revealed.VetoArmed = revealed.Kind == PageKind.Guarded && revealed.Param.Id == -1;
            BumpTo(revealed);
            m.EventCount++;
            m.LastEventInstance = revealed.Id;
            return true;
        }

        public void PredictClearCache(RegionModel m)
        {
            // RegionCacheCleared is raised only when the cache had entries.
            if (m.Cache.Count > 0)
            {
                m.CacheClearedCount++;
            }

            foreach (var entry in m.Cache.Values)
            {
                _disposed.Add(entry.Id);
                m.ReleasedCount++;
            }

            m.Cache.Clear();
        }

        public void PredictClearAllCache()
        {
            foreach (var m in _regions.Values)
            {
                PredictClearCache(m);
            }
        }

        /// <summary>
        /// Fills in the instance ids a landing produced and checks the
        /// creation/disposal bookkeeping of resolutions: a fresh landing
        /// creates exactly one instance (the page itself); a recognized
        /// landing without a cache claim creates exactly one throwaway,
        /// which must already be disposed; adoption and cache-claimed
        /// recognition create none.
        /// </summary>
        public void ReconcileLanding(
            RegionModel m, Plan plan, INavigationService service, ModelLedger ledger,
            int createdBefore, Action<bool, string> require)
        {
            if (plan.Landing is LandingKind.None or LandingKind.Refreshed)
            {
                require(ledger.CreatedCount == createdBefore,
                    $"No instance may be created by a {plan.Landing} outcome; delta {ledger.CreatedCount - createdBefore}.");
                return;
            }

            var created = ledger.CreatedSnapshot();
            var newIds = created.Keys.Where(id => id > createdBefore).OrderBy(id => id).ToList();

            if (plan.Landing == LandingKind.Fresh)
            {
                require(newIds.Count == 1, $"Fresh landing must create exactly one instance; got {newIds.Count}.");
                var top = service.GetCurrentViewModel(m.Name);
                require(top is ModelPageBase, "Fresh landing: current page is not a model page.");
                int freshId = ((ModelPageBase)top!).InstanceId;
                require(newIds[0] == freshId, $"Fresh landing created {newIds[0]} but current page is {freshId}.");
                plan.LandedEntry!.Id = freshId;
                _counts[freshId] = (1, 0);
                if (m.LastEventInstance == -1)
                {
                    m.LastEventInstance = freshId;
                }
            }
            else if (plan.Landing == LandingKind.Recognized && plan.ExpectThrowaway)
            {
                require(newIds.Count == 1, $"Recognized landing must discard exactly one throwaway; got {newIds.Count} new.");
                require(ledger.DisposedSnapshot().Contains(newIds[0]),
                    $"Throwaway instance {newIds[0]} was not disposed with its scope.");
                _disposed.Add(newIds[0]);
                ThrowawayCount++;
            }
            else
            {
                require(newIds.Count == 0, $"A {plan.Landing} landing must not create instances; got {newIds.Count}.");
            }
        }

        public void AssertState(
            INavigationService service, ModelLedger ledger,
            Dictionary<string, int> eventCounts, Dictionary<string, int> lastEventInstance,
            Dictionary<string, int> cacheClearedCounts, Dictionary<string, int> releasedCounts,
            Action<bool, string> require)
        {
            var created = ledger.CreatedSnapshot();
            var disposed = ledger.DisposedSnapshot();

            foreach (var region in Regions)
            {
                var m = _regions[region];
                var actualStack = service.GetNavigationStack(region);
                require(actualStack.Count == m.Stack.Count,
                    $"{region}: stack depth model {m.Stack.Count}, actual {actualStack.Count}.");
                for (int i = 0; i < m.Stack.Count && i < actualStack.Count; i++)
                {
                    var page = (ModelPageBase)actualStack[i];
                    require(page.InstanceId == m.Stack[i].Id,
                        $"{region}: stack[{i}] model instance {m.Stack[i].Id}, actual {page.InstanceId}.");
                    require(Equals(page.LastParam, m.Stack[i].Param),
                        $"{region}: stack[{i}] (instance {page.InstanceId}) stored parameter model {m.Stack[i].Param}, actual {page.LastParam}.");
                }

                var current = service.GetCurrentViewModel(region);
                var topModel = m.Stack.LastOrDefault();
                require((current as ModelPageBase)?.InstanceId == topModel?.Id,
                    $"{region}: current model {topModel?.Id.ToString() ?? "null"}, actual {(current as ModelPageBase)?.InstanceId.ToString() ?? "null"}.");
                require(service.GetCurrentRetention(region) == topModel?.Retention,
                    $"{region}: current retention model {topModel?.Retention.ToString() ?? "null"}, actual {service.GetCurrentRetention(region)?.ToString() ?? "null"}.");
                require(service.CanGoBack(region) == (m.Stack.Count > 1),
                    $"{region}: CanGoBack model {m.Stack.Count > 1}, actual {service.CanGoBack(region)}.");
                require(eventCounts[region] == m.EventCount,
                    $"{region}: RegionNavigated count model {m.EventCount}, actual {eventCounts[region]}.");
                require(lastEventInstance[region] == m.LastEventInstance,
                    $"{region}: last RegionNavigated instance model {m.LastEventInstance}, actual {lastEventInstance[region]}.");
                require(cacheClearedCounts[region] == m.CacheClearedCount,
                    $"{region}: RegionCacheCleared count model {m.CacheClearedCount}, actual {cacheClearedCounts[region]}.");
                require(releasedCounts[region] == m.ReleasedCount,
                    $"{region}: ViewModelReleased count model {m.ReleasedCount}, actual {releasedCounts[region]}.");
            }

            foreach (var (id, page) in created)
            {
                var expected = Counts(id);
                require(page.ToCount == expected.To,
                    $"instance {id}: OnNavigatedTo count model {expected.To}, actual {page.ToCount}.");
                require(page.FromCount == expected.From,
                    $"instance {id}: OnNavigatedFrom count model {expected.From}, actual {page.FromCount}.");
                require(disposed.Contains(id) == _disposed.Contains(id),
                    $"instance {id}: disposed model {_disposed.Contains(id)}, actual {disposed.Contains(id)}.");
            }
        }

        public string Dump(INavigationService service, ModelLedger ledger)
        {
            var lines = new List<string>();
            foreach (var region in Regions)
            {
                var m = _regions[region];
                string Fmt(IEnumerable<Entry> entries) => string.Join(", ",
                    entries.Select(e => $"{e.Kind}#{e.Id}({e.Param.Id},{e.Retention})"));
                var actual = service.GetNavigationStack(region)
                    .Select(vm => { var p = (ModelPageBase)vm; return $"{p.GetType().Name}#{p.InstanceId}({p.LastParam?.Id.ToString() ?? "null"})"; });
                lines.Add($"{region} model stack: [{Fmt(m.Stack)}] cache: [{Fmt(m.Cache.Values)}]");
                lines.Add($"{region} actual stack: [{string.Join(", ", actual)}]");
            }

            lines.Add($"model disposed: [{string.Join(", ", _disposed.OrderBy(i => i))}]");
            lines.Add($"actual disposed: [{string.Join(", ", ledger.DisposedSnapshot().OrderBy(i => i))}]");
            return string.Join("\n", lines);
        }
    }
}

/// <summary>Value-equal navigation parameter with a tiny collision domain.</summary>
public sealed record P(int Id);

/// <summary>Per-instance creation/disposal ledger shared by the model pages.</summary>
public sealed class ModelLedger
{
    private int _nextId;
    private readonly object _gate = new();
    private readonly Dictionary<int, ModelPageBase> _created = [];
    private readonly HashSet<int> _disposed = [];

    public int Register(ModelPageBase page)
    {
        int id = Interlocked.Increment(ref _nextId);
        lock (_gate)
        {
            _created[id] = page;
        }

        return id;
    }

    public void RecordDisposed(int id)
    {
        lock (_gate)
        {
            _disposed.Add(id);
        }
    }

    public int CreatedCount
    {
        get { lock (_gate) { return _created.Count; } }
    }

    public IReadOnlyDictionary<int, ModelPageBase> CreatedSnapshot()
    {
        lock (_gate)
        {
            return new Dictionary<int, ModelPageBase>(_created);
        }
    }

    public HashSet<int> DisposedSnapshot()
    {
        lock (_gate)
        {
            return [.. _disposed];
        }
    }
}

/// <summary>Base for the model-walk pages: synchronous lifecycle, per-instance counts.</summary>
public abstract class ModelPageBase : NavigationViewModelBase, INavigationAware, IDisposable
{
    private readonly ModelLedger _ledger;

    protected ModelPageBase(ModelLedger ledger)
    {
        _ledger = ledger;
        InstanceId = ledger.Register(this);
    }

    public int InstanceId { get; }

    public P? LastParam { get; private set; }

    public int ToCount { get; private set; }

    public int FromCount { get; private set; }

    public virtual void OnNavigatedTo(object? parameter)
    {
        ToCount++;
        LastParam = parameter as P;
    }

    public void OnNavigatedFrom() => FromCount++;

    public void Dispose() => _ledger.RecordDisposed(InstanceId);
}

public sealed class ModelPageA(ModelLedger ledger) : ModelPageBase(ledger);

public sealed class ModelPageB(ModelLedger ledger) : ModelPageBase(ledger);

/// <summary>
/// A page whose leave guard vetoes the first departure attempt of each
/// activation whose parameter is the sentinel P(-1); each activation
/// re-arms the one-shot veto.
/// </summary>
public sealed class ModelGuardedPage(ModelLedger ledger) : ModelPageBase(ledger), INavigationGuard
{
    private bool _vetoSpent;

    public override void OnNavigatedTo(object? parameter)
    {
        _vetoSpent = false;
        base.OnNavigatedTo(parameter);
    }

    public Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken)
    {
        if (LastParam?.Id == -1 && !_vetoSpent)
        {
            _vetoSpent = true;
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}
