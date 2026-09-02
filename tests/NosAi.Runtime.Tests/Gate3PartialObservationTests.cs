using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Safety;
using Xunit;

// VerificationResult still exists in both Contracts and Gate 3, and
// RuntimeSafetyPolicy is aliased so a broad Safety using does not collide
// with other types in that namespace.
using DataSourceKind = NosAi.Runtime.Contracts.DataSourceKind;
using RuntimeSafetyPolicy = NosAi.Runtime.Safety.RuntimeSafetyPolicy;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Tests;

/// <summary>
/// ADR-0016: what Gate 3 plans on when part of the world is unknown, and what it
/// acts on when the reading is real but no longer new.
/// </summary>
/// <remarks>
/// <para>
/// Both rules these tests pin were written when nothing real could reach them.
/// Planning demanded all five fields, so the observed network channel — which
/// establishes HP, max HP and MP and says nothing about targeting — could never
/// produce a single cycle; and one of the two fields it refused over is read by no
/// rule at all. Acting demanded all five be LIVE, so a reading republished CACHED
/// between two packets was refused as a <i>simulation</i>, which is a false thing
/// to tell an operator about a value observed a second ago.
/// </para>
/// <para>
/// What must not move is the property underneath: an unknown fact never selects a
/// branch, and a simulated one never reaches the world.
/// </para>
/// </remarks>
public sealed class Gate3PartialObservationTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class StubClock : TimeProvider
    {
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class CountingEffector : IActionEffector
    {
        public int Applications { get; private set; }
        public bool CanApply => true;
        public string? UnavailableReason => null;

        public Task<ExecutionResult> ApplyAsync(
            ActionCandidate candidate, SafetyToken token, CancellationToken cancellationToken = default)
        {
            Applications++;
            return Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 1, null));
        }
    }

    private static readonly RuntimeSafetyPolicy ExecutionAllowed = new(
        LiveInputEnabled: true, PacketInjectionEnabled: false, RequireClientHealthy: true, RequireGuardApproval: true);

    /// <summary>Vitals read, targeting never established — the network channel's shape.</summary>
    private static Gate3WorldState VitalsOnly(int hp, int maxHp, int mp, DataSourceKind source, DateTime at)
        => new(
            Classify(hp, source, at),
            Classify(maxHp, source, at),
            Classify(mp, source, at),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"));

    private static NosAi.Runtime.Contracts.ClassifiedValue<int> Classify(int value, DataSourceKind source, DateTime at) => source switch
    {
        DataSourceKind.Live => NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(value, at),
        DataSourceKind.Derived => NosAi.Runtime.Contracts.ClassifiedValue<int>.Derived(value, at),
        DataSourceKind.Cached => NosAi.Runtime.Contracts.ClassifiedValue<int>.Cached(value, at),
        _ => NosAi.Runtime.Contracts.ClassifiedValue<int>.Simulated(value, at),
    };

    // ------------------------------------------------------------- planning

    [Fact]
    public void Vitals_alone_make_a_state_plannable()
    {
        Gate3WorldState state = VitalsOnly(1200, 5000, 900, DataSourceKind.Live, Now);

        Assert.True(state.HasVitals);
        Assert.True(state.IsPlannable);
        Assert.Null(state.UnusableReason);
    }

    [Fact]
    public void An_unread_vital_still_refuses_everything()
    {
        var state = new Gate3WorldState(
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(1200, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Unknown("player_vitals_not_mapped"),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(900, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Live(true, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Live(true, Now));

        Assert.False(state.IsPlannable);
        Assert.Equal("player_vitals_not_mapped", state.UnusableReason);
    }

    /// <summary>
    /// The decision this ADR turns on. Everything needed to decide "drink a
    /// potion" is observed and checked; the targeting state is not, and used to
    /// stop the whole cycle.
    /// </summary>
    [Fact]
    public async Task A_critical_hp_is_acted_on_even_when_the_targeting_state_is_unknown()
    {
        var effector = new CountingEffector();
        var clock = new StubClock();
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, effector, clock: clock);

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            VitalsOnly(200, 5000, 900, DataSourceKind.Live, Now));

        Assert.NotEqual(CycleOutcome.NoWorldState, result.Outcome);
        Assert.NotEqual(ActionType.None, result.SelectedAction);
        Assert.Equal(1, effector.Applications);
    }

    /// <summary>
    /// And the branch that depends on the unknown fact is never taken. The
    /// exploration move is the one that matters: it is what "no target" means, and
    /// it would have the character walk away from a fight nobody has confirmed is
    /// not happening.
    /// </summary>
    [Fact]
    public async Task An_unknown_target_never_becomes_a_move_or_an_attack()
    {
        var effector = new CountingEffector();
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, effector, clock: new StubClock());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            VitalsOnly(4800, 5000, 900, DataSourceKind.Live, Now));

        Assert.Equal(CycleOutcome.NoCandidate, result.Outcome);
        Assert.Equal(ActionType.None, result.SelectedAction);
        Assert.Equal(0, effector.Applications);
    }

    /// <summary>
    /// Knowing there is no target is a fact, and it selects the branch that
    /// "unknown" must not.
    /// </summary>
    /// <remarks>
    /// What changed with C6-2 is where the exploration walks to. It used to be the
    /// constant <c>(130, 90)</c> — a point nobody had observed, carried since the
    /// rule needed somewhere to go — and it is now the place the active goal
    /// names. The branch is still selected by a known-false target; it simply has
    /// nowhere to walk unless something asked the runtime to look there, which is
    /// the next test.
    /// </remarks>
    [Fact]
    public void A_known_absent_target_still_plans_the_exploration_move()
    {
        var state = new Gate3WorldState(
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(4800, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(5000, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(900, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Live(false, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"));
        var planner = new ActionPlanner(
            GoalStack.With(Goal.Hunt("test-hunt", new[] { 36 }, new MapPoint(130, 90))));

        List<ActionCandidate> candidates = planner.PlanCandidates(state);

        ActionCandidate move = Assert.Single(candidates, c => c.Type == ActionType.MoveToPosition);
        Assert.Equal(new MapPoint(130, 90), Assert.IsType<ActionTarget.Position>(move.Target).At);
    }

    /// <summary>
    /// And with nothing asked of it, the same known-absent target plans nothing at
    /// all. The waypoint was the last place the runtime moved for no stated
    /// reason (C6-2).
    /// </summary>
    [Fact]
    public void A_known_absent_target_with_no_goal_plans_nothing()
    {
        var state = new Gate3WorldState(
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(4800, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(5000, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(900, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Live(false, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"));

        Assert.Empty(new ActionPlanner().PlanCandidates(state));
    }

    /// <summary>
    /// InCombat gates nothing, because no rule reads it. It was blocking the loop
    /// while changing no decision, which is the clearest single reason the old
    /// all-five rule was wrong.
    /// </summary>
    [Fact]
    public void An_unknown_combat_flag_changes_no_candidate()
    {
        var known = new Gate3WorldState(
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(1200, Now), NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(5000, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(900, Now), NosAi.Runtime.Contracts.ClassifiedValue<bool>.Live(true, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Live(true, Now));
        var unknown = known with { InCombat = NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("combat_flag_not_mapped") };

        var planner = new ActionPlanner();

        Assert.Equal(
            planner.PlanCandidates(known).Select(c => c.Type).OrderBy(t => t),
            planner.PlanCandidates(unknown).Select(c => c.Type).OrderBy(t => t));
    }

    // ------------------------------------------------------------- acting

    [Fact]
    public void A_cached_reading_is_not_a_simulation()
    {
        Gate3WorldState cached = VitalsOnly(1200, 5000, 900, DataSourceKind.Cached, Now);

        Assert.False(cached.IsSimulated);
        Assert.True(cached.IsActionable(Now.AddSeconds(1), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void A_reading_older_than_the_bound_cannot_act()
    {
        Gate3WorldState cached = VitalsOnly(1200, 5000, 900, DataSourceKind.Cached, Now);

        Assert.False(cached.IsActionable(Now.AddSeconds(2.5), TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(2.5), cached.AgeAt(Now.AddSeconds(2.5)));
    }

    /// <summary>A state is as old as its oldest field.</summary>
    [Fact]
    public void One_stale_field_ages_the_whole_state()
    {
        var mixed = new Gate3WorldState(
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Cached(1200, Now.AddSeconds(-10)),   // the stale one
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(5000, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(900, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"));

        Assert.Equal(Now.AddSeconds(-10), mixed.ObservedAtUtc);
        Assert.False(mixed.IsActionable(Now, TimeSpan.FromSeconds(2)));
    }

    /// <summary>
    /// The refusal has to name what actually happened. Telling the operator a
    /// reading taken 3 s ago is "simulated" is false, and it hides the age, which
    /// is the only number that makes the refusal diagnosable.
    /// </summary>
    [Fact]
    public async Task A_stale_reading_is_refused_as_stale_and_says_how_old()
    {
        var effector = new CountingEffector();
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, effector, maxObservationAge: TimeSpan.FromSeconds(2), clock: new StubClock());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            VitalsOnly(200, 5000, 900, DataSourceKind.Cached, Now.AddSeconds(-3)));

        Assert.Equal(CycleOutcome.RefusedStaleInput, result.Outcome);
        Assert.Contains("3,0s", result.Summary.Replace('.', ','), StringComparison.Ordinal);
        Assert.Equal(0, effector.Applications);
    }

    [Fact]
    public async Task A_simulated_reading_is_still_refused_as_simulated()
    {
        // Unchanged, and it must stay a separate outcome from staleness.
        var effector = new CountingEffector();
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, effector, clock: new StubClock());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            Gate3WorldState.Simulated(200, 5000, 900, true, true));

        Assert.Equal(CycleOutcome.RefusedSimulatedInput, result.Outcome);
        Assert.Equal(0, effector.Applications);
    }

    /// <summary>
    /// One simulated field is enough, however real the rest are: a plan built on it
    /// is still a plan built on something nobody observed.
    /// </summary>
    [Fact]
    public async Task One_simulated_field_among_real_ones_still_refuses()
    {
        var effector = new CountingEffector();
        var orchestrator = new Gate3ExecutionOrchestrator(
            ExecutionAllowed, effector, clock: new StubClock());

        var mostlyReal = new Gate3WorldState(
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(200, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Live(5000, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<int>.Simulated(900, Now),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            NosAi.Runtime.Contracts.ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"));

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(mostlyReal);

        Assert.Equal(CycleOutcome.RefusedSimulatedInput, result.Outcome);
        Assert.Equal(0, effector.Applications);
    }

    /// <summary>
    /// With nothing bound that can act, a stale or simulated state is planned on
    /// freely: the refusal protects the world, not the reasoning.
    /// </summary>
    [Fact]
    public async Task Staleness_does_not_stop_a_dry_run()
    {
        var orchestrator = new Gate3ExecutionOrchestrator(clock: new StubClock());

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            VitalsOnly(200, 5000, 900, DataSourceKind.Cached, Now.AddSeconds(-60)));

        Assert.Equal(CycleOutcome.ExecutionDisabled, result.Outcome);
        Assert.NotEqual(ActionType.None, result.SelectedAction);
    }

    /// <summary>
    /// A reading stamped in the future is a clock disagreement, not a fresh
    /// observation. Subtracting would make it look newer than new.
    /// </summary>
    [Fact]
    public void A_reading_from_the_future_is_not_treated_as_fresh()
    {
        Gate3WorldState ahead = VitalsOnly(1200, 5000, 900, DataSourceKind.Live, Now.AddSeconds(30));

        Assert.False(ahead.IsActionable(Now, TimeSpan.FromSeconds(2)));
    }
}
