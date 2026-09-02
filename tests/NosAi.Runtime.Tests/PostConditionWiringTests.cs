using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Safety;
using Xunit;
using TrustTier = NosAi.Runtime.Contracts.TrustTier;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The post-condition table on the cycle: what it refuses to run, what it now
/// accepts as verification, and the one action that is never repeated.
/// </summary>
/// <remarks>
/// The cards themselves are pinned by <see cref="PostConditionCatalogueTests"/>.
/// What is here is the wiring — the three places the catalogue changes the loop
/// rather than the predicate: admission, the verification tier (VER-04), and the
/// escalation § 4.8 asks for.
/// </remarks>
public sealed class PostConditionWiringTests
{
    private static readonly RuntimeSafetyPolicy ExecutionAllowed = new(
        LiveInputEnabled: true, PacketInjectionEnabled: false, RequireClientHealthy: true, RequireGuardApproval: true);

    /// <summary>
    /// A standing goal, so the cycles below can plan an attack at all.
    /// </summary>
    /// <remarks>
    /// C6-2 made an active goal the precondition of every proactive attack: with
    /// nothing asked of the runtime, no fight is picked and these cycles would
    /// plan nothing. The goal is not what these tests are about — that is
    /// <see cref="GoalStackTests"/> — it is the reason the runtime is allowed to
    /// be in the fight they set up.
    /// </remarks>
    private static GoalStack Hunting() => GoalStack.With(Goal.Hunt("test-hunt", new[] { 36 }));

    /// <summary>A world the planner answers with an attack: healthy, with a target.</summary>
    private static Gate3WorldState Fighting() => Gate3WorldState.Live(800, 1000, 100, true, false);

    // ------------------------------------------------------------- admission

    /// <summary>
    /// § 7's first property, on the loop: an action with no card is refused
    /// before anything touches the world, by name, rather than executed and then
    /// found unverifiable.
    /// </summary>
    [Fact]
    public async Task An_action_with_no_card_is_refused_before_the_effector_is_reached()
    {
        var effector = new CountingEffector();
        // A table that knows about everything except the skill this cycle picks.
        var partial = new PostConditionTable(
            new MoveToPositionPostCondition(),
            new TargetEntityPostCondition(),
            new UseBasicAttackPostCondition(),
            new UseConsumablePostCondition());
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, effector, postConditions: partial, goals: Hunting());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Fighting());

        Assert.Equal(CycleOutcome.Blocked, result.Outcome);
        Assert.Contains("no_post_condition:UseSkill", result.Summary, StringComparison.Ordinal);
        Assert.Equal(0, effector.Applications);
    }

    /// <summary>
    /// The shipping catalogue admits every action the planner can propose, so the
    /// refusal above is a guard and not a wall the runtime walks into.
    /// </summary>
    [Fact]
    public void The_shipping_catalogue_admits_every_action_the_planner_proposes()
    {
        var planner = new ActionPlanner(Hunting());
        var proposed = new HashSet<ActionType>();

        foreach (Gate3WorldState state in new[]
        {
            Gate3WorldState.Live(200, 1000, 100, true, false),   // critical, fighting
            Gate3WorldState.Live(800, 1000, 100, true, false),   // healthy, fighting
            Gate3WorldState.Live(800, 1000, 100, false, false),  // healthy, nothing to fight
        })
        {
            foreach (ActionCandidate candidate in planner.PlanCandidates(state))
                proposed.Add(candidate.Type);
        }

        Assert.NotEmpty(proposed);
        foreach (ActionType action in proposed)
            Assert.True(PostConditionTable.Catalogue.IsAdmissible(action), action.ToString());
    }

    // --------------------------------------------------------------- VER-04

    /// <summary>
    /// § 1.4, closed. The verifier used to demand both readings LIVE while
    /// ADR-0016 § 2 already let the runtime act on a fresh CACHED or DERIVED one,
    /// so a cycle could act on a reading it could never verify. A fresh CACHED
    /// reading now verifies, and the result says which provenance it rests on.
    /// </summary>
    [Fact]
    public async Task A_fresh_cached_reading_verifies_a_cycle_it_would_have_been_allowed_to_drive()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed,
            new CountingEffector(),
            new CachedVitalsObserver(hp: 800, mp: 40),
            goals: Hunting());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Fighting());

        // MP fell from 100 to 40, so the skill fired.
        Assert.Equal(CycleOutcome.Confirmed, result.Outcome);
        Assert.Contains("mp_fell", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tier is not lowered to nothing. A simulated reading still verifies
    /// nothing, and an unread one still verifies nothing.
    /// </summary>
    [Fact]
    public void A_simulated_or_unread_reading_is_still_not_usable_for_verification()
    {
        DateTime now = DateTime.UtcNow;
        var simulated = new ObservedState(
            ClassifiedValue<int>.Simulated(800, now), ClassifiedValue<int>.Simulated(40, now));
        ObservedState unread = ObservedState.Unobserved("no_perception");

        Assert.False(simulated.IsUsableForVerification(now, TimeSpan.FromSeconds(2)));
        Assert.False(unread.IsUsableForVerification(now, TimeSpan.FromSeconds(2)));
        // And a real reading that is simply too old is refused for its own reason.
        var stale = ObservedState.Live(800, 40, now.AddSeconds(-30));
        Assert.False(stale.IsUsableForVerification(now, TimeSpan.FromSeconds(2)));
        Assert.True(ObservedState.Live(800, 40, now).IsUsableForVerification(now, TimeSpan.FromSeconds(2)));
    }

    // ---------------------------------------------------- § 4.8, no repeats

    /// <summary>
    /// The case § 4.8 is really about, and the one that separates a card that
    /// forbids a retry from one that does not: a cycle nobody could verify.
    /// Under an ordinary card that is a replan; under this one it is a halt with
    /// an alarm, because a flight that cannot be checked is a flight taken in a
    /// situation that has by construction got worse.
    /// </summary>
    [Fact]
    public async Task An_unverifiable_cycle_of_an_action_that_forbids_a_retry_halts_instead_of_replanning()
    {
        // No observer, so nothing can be read back and no card can conclude.
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed,
            new CountingEffector(),
            postConditions: new PostConditionTable(new NeverRetriedSkill()),
            goals: Hunting());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Fighting());

        Assert.Equal(CycleOutcome.Failed, result.Outcome);
        Assert.Equal(RecoveryStrategy.HaltAndAlert, result.Strategy);
        Assert.Contains("Fuga non verificata", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// § 5's severest band, reached on its own merit: a promise fully
    /// contradicted is a hard stop whatever the breaker's history says and
    /// whatever the card is. The bands are a floor under the breaker, not a
    /// second opinion it can talk down.
    /// </summary>
    [Fact]
    public async Task A_promise_fully_contradicted_halts_on_the_band_alone()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed,
            new CountingEffector(),
            new CachedVitalsObserver(hp: 800, mp: 100),   // MP unchanged: the skill did not fire
            goals: Hunting());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Fighting());

        Assert.Equal(CycleOutcome.Failed, result.Outcome);
        Assert.Equal(RecoveryStrategy.HaltAndAlert, result.Strategy);
        // d = 1.0 is the top band, and the reason is the action's own predicate.
        Assert.Contains("mp_did_not_fall", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// VER-05 on the loop: an executed cycle nobody could check is never a
    /// success, and § 5 gives it a replan rather than a "carry on".
    /// </summary>
    [Fact]
    public async Task An_unverifiable_cycle_replans_and_never_continues()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, new CountingEffector(), goals: Hunting());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Fighting());

        Assert.Equal(CycleOutcome.Unverified, result.Outcome);
        Assert.False(result.IsConfirmed);
        Assert.Equal(RecoveryStrategy.Replan, result.Strategy);
    }

    // ------------------------------------------------------ C6-4, the breaker

    /// <summary>
    /// The last link the catalogue asked for: an action that produces no
    /// observable effect reaches the recovery breaker, so it degrades instead of
    /// being repeated. The band and the breaker's own history are combined by
    /// taking the severer of the two, so a clean history cannot soften a badly
    /// divergent cycle and a small divergence cannot soften a long run of
    /// failures.
    /// </summary>
    [Fact]
    public async Task An_action_that_produces_no_effect_reaches_the_breaker()
    {
        var recovery = new RecoveryController(new TrustBoundary(TrustTier.Tier2_SemiAutonomous));
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed,
            new CountingEffector(),
            new CachedVitalsObserver(hp: 800, mp: 100),   // MP unchanged, every cycle
            goals: Hunting(),
            recovery: recovery);

        Gate3CycleResult first = await orchestrator.ExecuteCycleAsync(Fighting());

        Assert.Equal(CycleOutcome.Failed, first.Outcome);
        // The breaker was told, which is what makes the next cycle a different
        // decision from this one rather than the same one again.
        Assert.True(recovery.FailuresInWindow > 0);
        Assert.NotNull(first.Strategy);
    }

    /// <summary>
    /// And a confirmed cycle does not feed it, so the breaker measures failures
    /// and not activity.
    /// </summary>
    [Fact]
    public async Task A_confirmed_cycle_does_not_feed_the_breaker()
    {
        var recovery = new RecoveryController(new TrustBoundary(TrustTier.Tier2_SemiAutonomous));
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed,
            new CountingEffector(),
            new CachedVitalsObserver(hp: 800, mp: 40),    // MP fell: the skill fired
            goals: Hunting(),
            recovery: recovery);

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Fighting());

        Assert.Equal(CycleOutcome.Confirmed, result.Outcome);
        Assert.Equal(0, recovery.FailuresInWindow);
    }

    // --------------------------------------------------------------- doubles

    /// <summary>An observer whose readings are real, remembered, and freshly stamped.</summary>
    private sealed class CachedVitalsObserver : IWorldStateObserver
    {
        private readonly int _hp;
        private readonly int _mp;
        public CachedVitalsObserver(int hp, int mp) { _hp = hp; _mp = mp; }
        public bool CanObserve => true;

        public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default)
        {
            DateTime now = DateTime.UtcNow;
            return Task.FromResult(new ObservedState(
                ClassifiedValue<int>.Cached(_hp, now), ClassifiedValue<int>.Cached(_mp, now)));
        }
    }

    /// <summary>The skill card, with the flight's no-retry rule bolted on.</summary>
    private sealed class NeverRetriedSkill : IPostCondition
    {
        private readonly UseSkillPostCondition _inner = new();
        public ActionType Action => _inner.Action;
        public TimeSpan Window => _inner.Window;
        public bool RetryForbidden => true;
        public PostConditionVerdict Evaluate(in PostConditionInput input) => _inner.Evaluate(input);
    }

    private sealed class CountingEffector : IActionEffector
    {
        public int Applications { get; private set; }
        public bool CanApply => true;
        public string? UnavailableReason => null;

        public Task<ExecutionResult> ApplyAsync(ActionCandidate candidate, CancellationToken cancellationToken = default)
        {
            Applications++;
            return Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 1, null));
        }
    }
}
