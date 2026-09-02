using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception.Network;
using Xunit;
using TrustTier = NosAi.Runtime.Autonomy.TrustTier;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Why the runtime fights: it was hit, or it was asked to. Nothing else.
/// </summary>
/// <remarks>
/// <para>
/// C6 is the capability the operator asked for first and the least obvious.
/// <b>Reactive</b>: being hit is an observed fact, and the answer is to hit back
/// at <i>whoever</i> hit — which needs the aggressor C1-2 stopped throwing away.
/// <b>Proactive</b>: attacking something that has not attacked first needs an
/// active goal that names what to look for. No goal, no attack; that is a rule
/// that refuses, not a recommendation.
/// </para>
/// <para>
/// And under both: an entity is attacked only when something <i>established</i>
/// it as attackable. The wire's type 3 is monster and NPC together, so no rule
/// built on the type could avoid the merchants — this one never asks what an
/// entity is (docs/TASTI_E_BERSAGLIO.md § 6.2).
/// </para>
/// </remarks>
public sealed class GoalAndReactionTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    private sealed class StubClock : TimeProvider
    {
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    // ------------------------------------------------------------ the goal

    [Fact]
    public void A_goal_that_names_nothing_to_look_for_cannot_be_built()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Goal.Hunt("empty", Array.Empty<int>()));

        Assert.Contains("at least one vnum", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_stack_is_pursuing_nothing_and_names_nothing()
    {
        GoalStack stack = GoalStack.Empty();

        Assert.False(stack.HasActiveGoal);
        Assert.Null(stack.Current);
        Assert.Null(stack.SearchAt);
        Assert.False(stack.Names(36));
        // A null vnum never matches: an entity nobody has read is not one a goal
        // named, it is one nothing is known about.
        Assert.False(GoalStack.With(Goal.Hunt("h", new[] { 36 })).Names(null));
    }

    [Fact]
    public void The_most_recent_goal_is_the_one_in_force_and_popping_restores_the_previous()
    {
        var stack = GoalStack.Empty();
        stack.Push(Goal.Hunt("first", new[] { 9 }, new MapPoint(10, 10)));
        stack.Push(Goal.Hunt("second", new[] { 36 }, new MapPoint(20, 20)));

        Assert.Equal("second", stack.Current!.Id);
        Assert.Equal(new MapPoint(20, 20), stack.SearchAt);
        // Both are in force: interrupting a hunt to do something else does not
        // forget what was being hunted.
        Assert.True(stack.Names(9));
        Assert.True(stack.Names(36));

        Assert.True(stack.TryPop(out Goal popped));
        Assert.Equal("second", popped.Id);
        Assert.Equal("first", stack.Current!.Id);
        Assert.Equal(new MapPoint(10, 10), stack.SearchAt);
    }

    /// <summary>
    /// A goal that names what to seek and not where to look has nowhere to walk,
    /// and says so by carrying null rather than a point nobody chose.
    /// </summary>
    [Fact]
    public void A_goal_with_no_place_names_no_place()
    {
        GoalStack stack = GoalStack.With(Goal.Hunt("anywhere", new[] { 36 }));

        Assert.True(stack.HasActiveGoal);
        Assert.Null(stack.SearchAt);
    }

    // ------------------------------------------------ C6-2, the precondition

    /// <summary>
    /// The rule, on the planner: nothing asked of the runtime, no fight picked.
    /// It replaces the constant waypoint <c>(130, 90)</c>, which was the last
    /// place the runtime went for no stated reason.
    /// </summary>
    [Fact]
    public void With_no_goal_a_healthy_character_plans_nothing_at_all()
    {
        var planner = new ActionPlanner();

        Assert.Empty(planner.PlanCandidates(Fighting()));
        Assert.Empty(planner.PlanCandidates(Idle()));
    }

    [Fact]
    public void With_a_goal_the_attack_rules_plan_again()
    {
        var planner = new ActionPlanner(GoalStack.With(Goal.Hunt("hunt", new[] { 36 })));

        List<ActionCandidate> candidates = planner.PlanCandidates(Fighting());

        Assert.Contains(candidates, c => c.Type == ActionType.UseSkill);
        Assert.Contains(candidates, c => c.Type == ActionType.UseBasicAttack);
    }

    /// <summary>
    /// Survival is not a fight the runtime picked, so it never needed a reason and
    /// still does not. A character at critical health with nothing asked of it
    /// still drinks.
    /// </summary>
    [Fact]
    public void Survival_plans_without_a_goal_because_it_picks_no_fight()
    {
        var planner = new ActionPlanner();

        List<ActionCandidate> candidates = planner.PlanCandidates(
            Gate3WorldState.Live(200, 1000, 900, hasTarget: true, inCombat: false));

        Assert.Contains(candidates, c => c.Type == ActionType.UseConsumable);
        Assert.DoesNotContain(candidates, c => c.Type == ActionType.UseSkill);
        Assert.DoesNotContain(candidates, c => c.Type == ActionType.UseBasicAttack);
    }

    // ------------------------------------------------------ C6-1, the reaction

    /// <summary>
    /// Hit, so hit back — at the entity that did it, not at whatever is nearest.
    /// The aggressor is the one C1-2 stopped discarding.
    /// </summary>
    [Fact]
    public void Being_hit_plans_a_counterattack_against_the_aggressor()
    {
        var planner = new ActionPlanner(clock: new StubClock());

        List<ActionCandidate> candidates = planner.PlanCandidates(
            Fighting() with
            {
                HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now),
                Entities = new[] { Entity(313816, 0.9, new MapPoint(105, 100)) },
            });

        ActionCandidate counter = Assert.Single(candidates, c => c.Type == ActionType.UseBasicAttack);
        var target = Assert.IsType<ActionTarget.Entity>(counter.Target);
        Assert.Equal(313816, target.EntityId);
        Assert.Equal(new MapPoint(105, 100), target.At);
        Assert.Contains("Contrattacco", counter.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// The aggression is its own reason. It needs no goal, because the character
    /// is already in a fight it did not choose.
    /// </summary>
    [Fact]
    public void A_counterattack_needs_no_goal()
    {
        var planner = new ActionPlanner(clock: new StubClock());

        List<ActionCandidate> candidates = planner.PlanCandidates(
            Fighting() with { HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now) });

        Assert.False(planner.Goals.HasActiveGoal);
        Assert.Single(candidates, c => c.Type == ActionType.UseBasicAttack);
    }

    /// <summary>
    /// The decay window C6-1 asks for. Past it the aggression is history, not a
    /// reason, and the runtime stops answering a fight that ended.
    /// </summary>
    [Fact]
    public void An_aggression_stops_being_a_reason_once_it_has_decayed()
    {
        var clock = new StubClock();
        var planner = new ActionPlanner(clock: clock, reaction: new ReactionPolicy(TimeSpan.FromSeconds(10)));
        Gate3WorldState hit = Fighting() with
        {
            HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now),
        };

        Assert.Single(planner.PlanCandidates(hit), c => c.Type == ActionType.UseBasicAttack);

        clock.Advance(TimeSpan.FromSeconds(11));

        Assert.DoesNotContain(planner.PlanCandidates(hit), c => c.Type == ActionType.UseBasicAttack);
    }

    /// <summary>A hit nobody could attribute names nobody, so nothing is planned.</summary>
    [Fact]
    public void An_unknown_aggressor_plans_no_counterattack()
    {
        var planner = new ActionPlanner(clock: new StubClock());

        List<ActionCandidate> candidates = planner.PlanCandidates(
            Fighting() with { HitBy = ClassifiedValue<Aggressor>.Unknown("player_entity_id_not_observed") });

        Assert.Empty(candidates);
    }

    /// <summary>A dead aggressor is not something to hit back at.</summary>
    [Fact]
    public void A_dead_aggressor_is_not_counterattacked()
    {
        var planner = new ActionPlanner(clock: new StubClock());

        List<ActionCandidate> candidates = planner.PlanCandidates(
            Fighting() with
            {
                HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now),
                Entities = new[] { Entity(313816, 0.0, new MapPoint(105, 100)) },
            });

        Assert.Empty(candidates);
    }

    /// <summary>
    /// The aggressor's position is carried when one has been observed and left
    /// null when none has. Null is not the map origin: the effector needs a point
    /// on screen and refuses by name rather than clicking at 0,0.
    /// </summary>
    [Fact]
    public void A_counterattack_on_an_unlocated_aggressor_carries_no_position()
    {
        var planner = new ActionPlanner(clock: new StubClock());

        List<ActionCandidate> candidates = planner.PlanCandidates(
            Fighting() with { HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now) });

        var target = Assert.IsType<ActionTarget.Entity>(Assert.Single(candidates).Target);
        Assert.Null(target.At);
    }

    // ------------------------------------------------ C6-3, what is established

    /// <summary>
    /// The strongest evidence there is: something that hit this character is
    /// beyond doubt something that fights.
    /// </summary>
    [Fact]
    public void An_entity_that_attacked_us_is_established()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Entity(313816, 0.9, new MapPoint(105, 100)),
            ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now),
            selected: null,
            catalogue: null);

        Assert.True(verdict.IsEstablished);
        Assert.Equal(TargetEvidence.AttackedUs, verdict.Evidence);
    }

    /// <summary>
    /// The character having acted on it is evidence the client accepted it as a
    /// target — which is a better classifier than anything derivable here.
    /// </summary>
    [Fact]
    public void An_entity_we_acted_on_is_established()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Entity(3205, null, new MapPoint(105, 100)),
            hitBy: null,
            ClassifiedValue<TargetedEntity>.Live(new TargetedEntity(3205, 3), Now),
            catalogue: null);

        Assert.True(verdict.IsEstablished);
        Assert.Equal(TargetEvidence.WeActedOnIt, verdict.Evidence);
    }

    /// <summary>
    /// The heart of § 6.2. An entity nothing has established is not attacked, and
    /// the reason is that nothing established it — never that it was recognised
    /// as something else. That is what makes the rule hold where the wire's type
    /// 3 makes classification impossible.
    /// </summary>
    [Fact]
    public void An_entity_nothing_established_is_not_attackable_and_the_reason_is_the_absence()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Entity(9999, 1.0, new MapPoint(105, 100), vnum: 2000),
            hitBy: null,
            selected: null,
            catalogue: null);

        Assert.False(verdict.IsEstablished);
        Assert.Equal(TargetEvidence.None, verdict.Evidence);
        Assert.Equal("reference_catalogue_not_loaded", verdict.Reason);
    }

    /// <summary>
    /// An entity whose vnum nobody has read cannot be established by the
    /// catalogue at all, and that is the common case: a capture that started
    /// mid-session has 25 spawns against 7 685 moves.
    /// </summary>
    [Fact]
    public void An_entity_with_no_vnum_read_is_not_established_by_the_catalogue()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Entity(9999, 1.0, new MapPoint(105, 100)),
            hitBy: null,
            selected: null,
            catalogue: null);

        Assert.False(verdict.IsEstablished);
        Assert.Equal(TargetEstablishment.VnumNotObservedReason, verdict.Reason);
    }

    /// <summary>
    /// A hit on a different entity establishes that entity and not this one. The
    /// evidence is per entity, never "there was a fight nearby".
    /// </summary>
    [Fact]
    public void Evidence_about_one_entity_does_not_establish_another()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Entity(313816, 0.9, new MapPoint(105, 100), vnum: 36),
            ClassifiedValue<Aggressor>.Live(new Aggressor(999999, 3), Now),
            ClassifiedValue<TargetedEntity>.Live(new TargetedEntity(888888, 3), Now),
            catalogue: null);

        Assert.False(verdict.IsEstablished);
    }

    // ------------------------------------- the selector refuses the unestablished

    /// <summary>
    /// The selector's own refusal: entities were observed, and none of them is
    /// something this runtime has been given a reason to attack. That reads quite
    /// differently from "there is nothing here", which is why it has its own name.
    /// </summary>
    [Fact]
    public void The_selector_refuses_when_nothing_observed_is_established()
    {
        SelectableEntity[] observed =
        [
            Entity(11, 1.0, new MapPoint(102, 100)),
            Entity(22, 1.0, new MapPoint(103, 100)),
        ];

        bool selected = TargetSelector.TrySelect(
            observed,
            ClassifiedValue<MapPoint>.Live(new MapPoint(100, 100), Now),
            Now,
            TargetSelectionPolicy.Default,
            out TargetChoice? choice,
            out string reason,
            isAttackable: _ => false);

        Assert.False(selected);
        Assert.Null(choice);
        Assert.Equal($"{TargetSelector.NothingEstablishedReason}:2", reason);
    }

    /// <summary>An established one behind unestablished ones is still chosen.</summary>
    [Fact]
    public void The_selector_picks_the_established_one_and_skips_the_rest()
    {
        SelectableEntity[] observed =
        [
            Entity(11, 1.0, new MapPoint(101, 100)),
            Entity(22, 1.0, new MapPoint(105, 100)),
        ];

        bool selected = TargetSelector.TrySelect(
            observed,
            ClassifiedValue<MapPoint>.Live(new MapPoint(100, 100), Now),
            Now,
            TargetSelectionPolicy.Default,
            out TargetChoice? choice,
            out string reason,
            isAttackable: e => e.EntityId == 22);

        Assert.True(selected, reason);
        Assert.Equal(22, choice!.Entity.EntityId);
    }

    /// <summary>
    /// With no filter the selector behaves as it always did, so a caller that has
    /// already filtered is not filtered twice.
    /// </summary>
    [Fact]
    public void With_no_filter_the_selector_selects_from_everything_observed()
    {
        SelectableEntity[] observed = [Entity(11, 1.0, new MapPoint(101, 100))];

        Assert.True(TargetSelector.TrySelect(
            observed,
            ClassifiedValue<MapPoint>.Live(new MapPoint(100, 100), Now),
            Now,
            TargetSelectionPolicy.Default,
            out TargetChoice? choice,
            out _));

        Assert.Equal(11, choice!.Entity.EntityId);
    }

    // ------------------------------------------- the two rules on the planner

    /// <summary>
    /// The proactive selection needs both: the entity established, and the goal
    /// naming it. An established entity the goal did not ask for is not chosen —
    /// otherwise "hunt vnum 36" would mean "fight whatever has been established".
    /// </summary>
    [Fact]
    public void A_new_target_is_chosen_only_when_the_goal_names_it_and_it_is_established()
    {
        var planner = new ActionPlanner(
            GoalStack.With(Goal.Hunt("hunt", new[] { 36 })), clock: new StubClock());

        // Established by our own ct, and its vnum is the one the goal asked for.
        Gate3WorldState named = Idle() with
        {
            Entities = new[] { Entity(313816, 1.0, new MapPoint(102, 100), vnum: 36) },
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(100, 100), Now),
            SelectedTarget = ClassifiedValue<TargetedEntity>.Live(new TargetedEntity(313816, 3), Now),
        };
        // Established the same way, and a vnum the goal never mentioned.
        Gate3WorldState unnamed = named with
        {
            Entities = new[] { Entity(313816, 1.0, new MapPoint(102, 100), vnum: 45) },
        };

        Assert.Contains(planner.PlanCandidates(named), c => c.Type == ActionType.TargetEntity);
        Assert.DoesNotContain(planner.PlanCandidates(unnamed), c => c.Type == ActionType.TargetEntity);
    }

    /// <summary>
    /// And the goal naming it is not enough on its own. A vnum in the goal with
    /// nothing establishing the entity is still an entity nothing established.
    /// </summary>
    [Fact]
    public void A_goal_naming_a_vnum_does_not_by_itself_establish_an_entity()
    {
        var planner = new ActionPlanner(
            GoalStack.With(Goal.Hunt("hunt", new[] { 36 })), clock: new StubClock());

        Gate3WorldState state = Idle() with
        {
            Entities = new[] { Entity(313816, 1.0, new MapPoint(102, 100), vnum: 36) },
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(100, 100), Now),
        };

        Assert.DoesNotContain(planner.PlanCandidates(state), c => c.Type == ActionType.TargetEntity);
    }

    // ------------------------------------------------------------- helpers

    private static Gate3WorldState Fighting() =>
        Gate3WorldState.Live(800, 1000, 100, hasTarget: true, inCombat: false, Now);

    private static Gate3WorldState Idle() =>
        Gate3WorldState.Live(800, 1000, 100, hasTarget: false, inCombat: false, Now);

    private static SelectableEntity Entity(long id, double? hp, MapPoint at, int? vnum = null) =>
        new(id, at, hp, Now, vnum);
}
