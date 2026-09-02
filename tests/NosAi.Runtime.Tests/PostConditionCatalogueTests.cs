using System.Reflection;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception.Network;
using Xunit;
using TrustTier = NosAi.Runtime.Autonomy.TrustTier;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The catalogue of post-conditions: what each action promises, and the nine
/// rules every card obeys (docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md).
/// </summary>
/// <remarks>
/// <para>
/// The four defects § 1 records are what this file exists to keep closed. For
/// half the actions the old predicate was "nothing changed"; the attack was
/// checked against the player's own HP instead of the target's; exact equality
/// made the predicate false in both directions; and the verification tier was
/// stricter than the actuation tier, so every screen-driven cycle ended
/// unverified by rule rather than by observation.
/// </para>
/// <para>
/// Each card gets the three answers it can give — kept, contradicted, and not
/// observable — because the third is the one a wrong design turns into one of the
/// first two.
/// </para>
/// </remarks>
public sealed class PostConditionCatalogueTests
{
    private static readonly DateTime Dispatch = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Before = Dispatch.AddMilliseconds(-100);
    private static readonly DateTime After = Dispatch.AddMilliseconds(100);
    private static readonly DateTime Later = Dispatch.AddMilliseconds(200);

    // ------------------------------------------------------------- the table

    /// <summary>
    /// The first of the two properties § 7 asks the tests to fix: nothing
    /// executes that nothing can check.
    /// </summary>
    [Theory]
    [InlineData(ActionType.MoveToPosition)]
    [InlineData(ActionType.TargetEntity)]
    [InlineData(ActionType.UseBasicAttack)]
    [InlineData(ActionType.UseSkill)]
    [InlineData(ActionType.UseConsumable)]
    [InlineData(ActionType.CollectGroundItem)]
    [InlineData(ActionType.EmergencyFlee)]
    public void Every_action_with_a_card_declares_its_own_window_and_action(ActionType action)
    {
        Assert.True(PostConditionTable.Catalogue.TryGet(action, out IPostCondition card));

        Assert.Equal(action, card.Action);
        // VER-06: the window belongs to the action. A card whose window is zero
        // would be checked over no time at all.
        Assert.True(card.Window > TimeSpan.Zero);
    }

    /// <summary>
    /// <c>RestAndRecover</c> has no card, and that is a decision rather than an
    /// omission: § 4.7 says its predicate is to be written when a gesture exists
    /// and not before. With no card it is not admissible, which is the refusal
    /// the catalogue wants — by name, at admission.
    /// </summary>
    [Theory]
    [InlineData(ActionType.RestAndRecover)]
    [InlineData(ActionType.None)]
    public void An_action_with_no_card_is_not_admissible_and_says_so_by_name(ActionType action)
    {
        Assert.False(PostConditionTable.Catalogue.TryGet(action, out _));
        Assert.False(PostConditionTable.Catalogue.IsAdmissible(action));
        Assert.Equal($"no_post_condition:{action}", PostConditionTable.RefusalReason(action));
    }

    [Fact]
    public void Two_cards_for_one_action_is_refused_at_construction()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new PostConditionTable(new UseSkillPostCondition(), new UseSkillPostCondition()));

        Assert.Contains("UseSkill", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second property § 7 asks for, and it is checked on the signature
    /// rather than on behaviour: VER-01 is impossible to violate because the
    /// prediction is unreachable from inside <c>Evaluate</c>.
    /// </summary>
    [Fact]
    public void Evaluate_cannot_see_the_prediction_because_it_is_not_a_parameter()
    {
        MethodInfo evaluate = typeof(IPostCondition).GetMethod(nameof(IPostCondition.Evaluate))!;

        ParameterInfo parameter = Assert.Single(evaluate.GetParameters());
        Assert.Equal(typeof(PostConditionInput).MakeByRefType(), parameter.ParameterType);

        // And the input itself carries no prediction, so it cannot arrive that way
        // either.
        Assert.DoesNotContain(
            typeof(PostConditionInput).GetProperties(),
            p => p.PropertyType == typeof(PredictedOutcome));
    }

    /// <summary>
    /// Every window in the catalogue is declared, not measured, and each says so.
    /// The day one is measured against a recording this fails and the flag moves
    /// with it.
    /// </summary>
    [Fact]
    public void Every_window_declares_itself_unmeasured_while_it_is()
    {
        foreach (ActionType action in Enum.GetValues<ActionType>())
        {
            if (!PostConditionTable.Catalogue.TryGet(action, out IPostCondition card)) continue;
            Assert.False(card.WindowIsMeasured);
        }
    }

    /// <summary>Only the flight forbids a retry (§ 4.8).</summary>
    [Fact]
    public void Only_the_flight_forbids_a_retry()
    {
        foreach (ActionType action in Enum.GetValues<ActionType>())
        {
            if (!PostConditionTable.Catalogue.TryGet(action, out IPostCondition card)) continue;
            Assert.Equal(action == ActionType.EmergencyFlee, card.RetryForbidden);
        }
    }

    // ------------------------------------------------------- § 5, the bands

    [Theory]
    [InlineData(0.00f, VerificationOutcome.Confirmed, null)]
    [InlineData(0.14f, VerificationOutcome.Confirmed, null)]
    [InlineData(0.15f, VerificationOutcome.Discrepant, RecoveryStrategy.Replan)]
    [InlineData(0.39f, VerificationOutcome.Discrepant, RecoveryStrategy.Replan)]
    [InlineData(0.40f, VerificationOutcome.Discrepant, RecoveryStrategy.Cooling)]
    [InlineData(0.69f, VerificationOutcome.Discrepant, RecoveryStrategy.Cooling)]
    [InlineData(0.70f, VerificationOutcome.Discrepant, RecoveryStrategy.HaltAndAlert)]
    [InlineData(1.00f, VerificationOutcome.Discrepant, RecoveryStrategy.HaltAndAlert)]
    public void The_bands_map_a_divergence_to_an_outcome_and_a_next_step(
        float divergence, VerificationOutcome outcome, RecoveryStrategy? next)
    {
        Assert.Equal(outcome, DivergenceBands.Outcome(divergence));
        Assert.Equal(next, DivergenceBands.Next(divergence));
    }

    // ------------------------------------------------------------- § 4.1 move

    [Fact]
    public void A_move_that_closes_the_distance_is_confirmed()
    {
        PostConditionVerdict verdict = Evaluate(
            Move(new MapPoint(110, 100)),
            States(At(Before, position: new MapPoint(100, 100)), At(After, position: new MapPoint(110, 100))));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
        Assert.Equal(0.0f, verdict.Divergence, 3);
    }

    /// <summary>
    /// The cell did not change: something the grid does not carry is in the way,
    /// and repeating the click would meet it again.
    /// </summary>
    [Fact]
    public void A_move_that_did_not_change_the_cell_is_a_stall_and_says_so()
    {
        PostConditionVerdict verdict = Evaluate(
            Move(new MapPoint(110, 100)),
            States(At(Before, position: new MapPoint(100, 100)), At(After, position: new MapPoint(100, 100))));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Equal("move_stalled_cell_unchanged", verdict.Reason);
    }

    [Fact]
    public void A_move_that_went_the_other_way_is_a_deviation_and_says_so()
    {
        PostConditionVerdict verdict = Evaluate(
            Move(new MapPoint(110, 100)),
            States(At(Before, position: new MapPoint(100, 100)), At(After, position: new MapPoint(90, 100))));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Equal("move_deviated_away_from_destination", verdict.Reason);
    }

    /// <summary>
    /// § 1.1, the defect this card replaces: the old predicate was "HP and MP
    /// unchanged", so a blow taken during a successful step failed the step. HP
    /// is not in this predicate at all.
    /// </summary>
    [Fact]
    public void A_blow_taken_during_a_successful_move_does_not_fail_the_move()
    {
        PostConditionVerdict verdict = Evaluate(
            Move(new MapPoint(110, 100)),
            States(
                At(Before, hp: 1000, position: new MapPoint(100, 100)),
                At(After, hp: 400, position: new MapPoint(110, 100))));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
    }

    [Fact]
    public void A_move_with_no_position_after_the_act_is_unverified()
    {
        PostConditionVerdict verdict = Evaluate(
            Move(new MapPoint(110, 100)),
            States(At(Before, position: new MapPoint(100, 100)), At(After)));

        Assert.Equal(VerificationOutcome.Unverified, verdict.Outcome);
        Assert.Equal("player_position_not_observed_after_dispatch", verdict.Reason);
    }

    // ----------------------------------------------------------- § 4.2 target

    [Fact]
    public void A_target_frame_that_appeared_confirms_the_selection()
    {
        PostConditionVerdict verdict = Evaluate(
            Target(entityId: 42),
            States(At(Before), At(After, hasTarget: ClassifiedValue<bool>.Derived(true, After))));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
    }

    [Fact]
    public void A_target_frame_that_stayed_absent_contradicts_the_selection()
    {
        PostConditionVerdict verdict = Evaluate(
            Target(entityId: 42),
            States(At(Before), At(After, hasTarget: ClassifiedValue<bool>.Derived(false, After))));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Equal(1.0f, verdict.Divergence, 3);
    }

    /// <summary>
    /// ADR-0018's three outcomes are three here too. An unreadable frame is never
    /// a failure: turning it into one is a confident wrong answer about the fact
    /// that decides whether the runtime is in a fight.
    /// </summary>
    [Fact]
    public void An_unreadable_target_frame_is_unverified_and_carries_the_readers_reason()
    {
        PostConditionVerdict verdict = Evaluate(
            Target(entityId: 42),
            States(
                At(Before),
                At(After, hasTarget: ClassifiedValue<bool>.Unknown("target_roi_not_calibrated"))));

        Assert.Equal(VerificationOutcome.Unverified, verdict.Outcome);
        Assert.Equal("target_roi_not_calibrated", verdict.Reason);
    }

    // ----------------------------------------------------------- § 4.3 attack

    /// <summary>
    /// VER-07: the subject is the target, never the character. The old prediction
    /// expected the <i>player</i> to lose 15 HP, a retaliation nobody measured.
    /// </summary>
    [Fact]
    public void An_attack_is_confirmed_by_the_targets_health_falling_not_the_players()
    {
        PostConditionVerdict verdict = Evaluate(
            Attack(entityId: 313816),
            States(
                At(Before, entities: new[] { Entity(313816, 0.90, Before) }),
                At(After, entities: new[] { Entity(313816, 0.40, After) })));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
        Assert.Contains("target_health_fell", verdict.Reason, StringComparison.Ordinal);
    }

    /// <summary>The player losing health is not the attack's effect, in either direction.</summary>
    [Fact]
    public void The_players_own_health_decides_nothing_about_an_attack()
    {
        PostConditionVerdict verdict = Evaluate(
            Attack(entityId: 313816),
            States(
                At(Before, hp: 1000, entities: new[] { Entity(313816, 0.90, Before) }),
                At(After, hp: 200, entities: new[] { Entity(313816, 0.90, After) })));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Equal("target_health_did_not_fall", verdict.Reason);
    }

    [Fact]
    public void An_attack_is_confirmed_by_a_death_whatever_the_health_readings_said()
    {
        PostConditionVerdict verdict = Evaluate(
            Attack(entityId: 313816),
            States(At(Before), At(After)),
            deaths: new long[] { 313816 });

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
        Assert.Equal("target_died", verdict.Reason);
    }

    /// <summary>
    /// A sighting without health is the common case — 7 685 of 8 211 packets are
    /// moves — and it is not an unchanged health. Nobody looked, so nothing is
    /// concluded.
    /// </summary>
    [Fact]
    public void A_target_seen_without_health_is_unverified_and_never_a_failure()
    {
        PostConditionVerdict verdict = Evaluate(
            Attack(entityId: 313816),
            States(
                At(Before, entities: new[] { Entity(313816, 0.90, Before) }),
                At(After, entities: new[] { Entity(313816, null, After) })));

        Assert.Equal(VerificationOutcome.Unverified, verdict.Outcome);
        Assert.Equal("target_health_not_observed_in_window", verdict.Reason);
    }

    [Fact]
    public void A_target_never_sighted_in_the_window_is_unverified()
    {
        PostConditionVerdict verdict = Evaluate(
            Attack(entityId: 313816),
            States(At(Before, entities: new[] { Entity(313816, 0.90, Before) }), At(After)));

        Assert.Equal(VerificationOutcome.Unverified, verdict.Outcome);
        Assert.Equal("target_not_sighted_in_window", verdict.Reason);
    }

    /// <summary>
    /// VER-09: the minimum across the window, not the last reading. A target hit
    /// and then healed inside one window was still hit.
    /// </summary>
    [Fact]
    public void An_attack_is_judged_on_the_lowest_health_in_the_window_not_the_last()
    {
        PostConditionVerdict verdict = Evaluate(
            Attack(entityId: 313816),
            States(
                At(Before, entities: new[] { Entity(313816, 0.90, Before) }),
                At(After, entities: new[] { Entity(313816, 0.30, After) }),
                At(Later, entities: new[] { Entity(313816, 0.95, Later) })));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
    }

    // ------------------------------------------------------------ § 4.4 skill

    [Fact]
    public void A_skill_whose_mp_fell_is_confirmed_and_still_names_the_blind_half()
    {
        PostConditionVerdict verdict = Evaluate(
            Skill(entityId: 313816),
            States(At(Before, mp: 1420), At(After, mp: 1385)));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
        // VER-08: the half nobody can observe is declared, never assumed. sr
        // reports a cooldown ending, not one beginning.
        Assert.Contains(UseSkillPostCondition.CooldownNotObservable, verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skill_whose_mp_did_not_fall_did_not_fire()
    {
        PostConditionVerdict verdict = Evaluate(
            Skill(entityId: 313816),
            States(At(Before, mp: 1420), At(After, mp: 1420)));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Contains("mp_did_not_fall", verdict.Reason, StringComparison.Ordinal);
    }

    /// <summary>VER-09 again, on the other side: MP spent and regenerated was still spent.</summary>
    [Fact]
    public void Mp_spent_and_regenerated_inside_the_window_still_counts_as_spent()
    {
        PostConditionVerdict verdict = Evaluate(
            Skill(entityId: 313816),
            States(At(Before, mp: 1420), At(After, mp: 1385), At(Later, mp: 1420)));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
    }

    // ------------------------------------------------------- § 4.5 consumable

    [Fact]
    public void A_potion_that_raised_the_hp_is_confirmed_whatever_the_amount()
    {
        PostConditionVerdict verdict = Evaluate(
            Consumable(slot: 1),
            States(At(Before, hp: 400), At(After, hp: 431)));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
        Assert.Contains("hp_rose", verdict.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// § 1.3: the old prediction demanded exactly +300 HP and +150 MP, so a
    /// potion that gave a different number failed and so did a blow taken in the
    /// same instant. The direction is what is checked.
    /// </summary>
    [Fact]
    public void A_potion_is_not_required_to_return_an_exact_amount()
    {
        foreach (int healed in new[] { 1, 42, 300, 999 })
        {
            PostConditionVerdict verdict = Evaluate(
                Consumable(slot: 1),
                States(At(Before, hp: 400), At(After, hp: 400 + healed)));

            Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
        }
    }

    /// <summary>
    /// VER-09 in full: a heal undone by a blow before the window closed still
    /// shows in the maximum, which is the quantity the action produced.
    /// </summary>
    [Fact]
    public void A_heal_undone_by_a_blow_before_the_window_closed_is_still_a_heal()
    {
        PostConditionVerdict verdict = Evaluate(
            Consumable(slot: 1),
            States(At(Before, hp: 400), At(After, hp: 700), At(Later, hp: 350)));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
    }

    /// <summary>The two causes this source cannot separate are named, not chosen between.</summary>
    [Fact]
    public void No_rise_at_all_is_reported_as_the_ambiguity_it_is()
    {
        PostConditionVerdict verdict = Evaluate(
            Consumable(slot: 1),
            States(At(Before, hp: 400, mp: 100), At(After, hp: 350, mp: 100)));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Equal(UseConsumablePostCondition.Ambiguous, verdict.Reason);
    }

    // -------------------------------------------------------- § 4.6 collect

    [Fact]
    public void A_collection_is_confirmed_when_an_inventory_slot_gains()
    {
        PostConditionVerdict verdict = Evaluate(
            Collect(new MapPoint(110, 63)),
            States(
                At(Before, inventory: new[] { Slot(34, 2006, 1, Before) }),
                At(After, inventory: new[] { Slot(34, 2006, 2, After) })));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
        Assert.Contains("inventory_slot_rose", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_collection_that_gained_nothing_is_a_discrepancy()
    {
        PostConditionVerdict verdict = Evaluate(
            Collect(new MapPoint(110, 63)),
            States(
                At(Before, inventory: new[] { Slot(34, 2006, 1, Before) }),
                At(After, inventory: new[] { Slot(34, 2006, 1, After) })));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Equal("inventory_did_not_gain", verdict.Reason);
    }

    [Fact]
    public void A_collection_with_no_inventory_reading_is_unverified()
    {
        PostConditionVerdict verdict = Evaluate(
            Collect(new MapPoint(110, 63)),
            States(At(Before), At(After)));

        Assert.Equal(VerificationOutcome.Unverified, verdict.Outcome);
        Assert.Equal("inventory_not_observed_at_dispatch", verdict.Reason);
    }

    // ----------------------------------------------------------- § 4.8 flee

    [Fact]
    public void A_flight_that_opened_the_distance_is_confirmed()
    {
        PostConditionVerdict verdict = Evaluate(
            Flee(new MapPoint(90, 100)),
            States(
                At(Before, position: new MapPoint(100, 100), entities: new[] { Entity(1, 1.0, Before, 101, 100) }),
                At(After, position: new MapPoint(90, 100), entities: new[] { Entity(1, 1.0, After, 101, 100) })));

        Assert.Equal(VerificationOutcome.Confirmed, verdict.Outcome);
    }

    [Fact]
    public void A_flight_that_did_not_open_the_distance_is_a_discrepancy()
    {
        PostConditionVerdict verdict = Evaluate(
            Flee(new MapPoint(90, 100)),
            States(
                At(Before, position: new MapPoint(100, 100), entities: new[] { Entity(1, 1.0, Before, 101, 100) }),
                At(After, position: new MapPoint(100, 100), entities: new[] { Entity(1, 1.0, After, 101, 100) })));

        Assert.Equal(VerificationOutcome.Discrepant, verdict.Outcome);
        Assert.Contains("flee_did_not_open_distance", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flight_with_no_hostile_observed_is_unverified()
    {
        PostConditionVerdict verdict = Evaluate(
            Flee(new MapPoint(90, 100)),
            States(At(Before, position: new MapPoint(100, 100)), At(After, position: new MapPoint(90, 100))));

        Assert.Equal(VerificationOutcome.Unverified, verdict.Outcome);
        Assert.Equal("no_hostile_observed_at_dispatch", verdict.Reason);
    }

    // ------------------------------------------------------------ VER-03

    /// <summary>
    /// A reading taken before the act describes a world the action had not
    /// touched. Every card refuses it the same way.
    /// </summary>
    [Fact]
    public void A_series_holding_nothing_after_the_act_confirms_nothing()
    {
        PostConditionVerdict move = Evaluate(
            Move(new MapPoint(110, 100)),
            States(At(Before, position: new MapPoint(100, 100)), At(Before, position: new MapPoint(110, 100))));
        PostConditionVerdict skill = Evaluate(
            Skill(entityId: 1), States(At(Before, mp: 1420), At(Before, mp: 10)));
        PostConditionVerdict potion = Evaluate(
            Consumable(slot: 1), States(At(Before, hp: 400), At(Before, hp: 900)));

        Assert.Equal(VerificationOutcome.Unverified, move.Outcome);
        Assert.Equal(VerificationOutcome.Unverified, skill.Outcome);
        Assert.Equal(VerificationOutcome.Unverified, potion.Outcome);
    }

    // ------------------------------------------------------------ VER-05

    /// <summary>
    /// Not observable is neither success nor failure. It never confirms, and the
    /// recovery breaker does not count it — <c>VerificationResult.CountsAsFailure</c>
    /// already excludes it, and this pins that the cards produce that outcome
    /// rather than a divergence of one.
    /// </summary>
    [Fact]
    public void An_unverifiable_verdict_carries_no_divergence_to_be_misread_as_a_failure()
    {
        PostConditionVerdict verdict = Evaluate(
            Attack(entityId: 313816), States(At(Before), At(After)));

        Assert.Equal(VerificationOutcome.Unverified, verdict.Outcome);
        Assert.Equal(0.0f, verdict.Divergence, 3);
        Assert.NotEqual(VerificationOutcome.Confirmed, verdict.Outcome);
    }

    // ------------------------------------------------------------- helpers

    private static PostConditionVerdict Evaluate(
        ActionCandidate candidate, IReadOnlyList<Gate3WorldState> states, IReadOnlyList<long>? deaths = null)
    {
        Assert.True(PostConditionTable.Catalogue.TryGet(candidate.Type, out IPostCondition card));

        var sightings = new List<SelectableEntity>();
        foreach (Gate3WorldState state in states)
            if (state.Entities is { } entities) sightings.AddRange(entities);

        return card.Evaluate(new PostConditionInput(candidate, Dispatch, states, sightings, deaths));
    }

    private static IReadOnlyList<Gate3WorldState> States(params Gate3WorldState[] states) => states;

    /// <summary>One reading, with only the fields a card is being asked about.</summary>
    private static Gate3WorldState At(
        DateTime at,
        int hp = 1000,
        int mp = 1420,
        MapPoint? position = null,
        IReadOnlyList<SelectableEntity>? entities = null,
        IReadOnlyList<InventorySlotReading>? inventory = null,
        ClassifiedValue<bool>? hasTarget = null) => new(
        Hp: ClassifiedValue<int>.Live(hp, at),
        MaxHp: ClassifiedValue<int>.Live(2000, at),
        Mp: ClassifiedValue<int>.Live(mp, at),
        HasTarget: hasTarget ?? ClassifiedValue<bool>.Unknown("target_roi_not_calibrated"),
        InCombat: ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
        Entities: entities,
        PlayerPosition: position is { } point
            ? ClassifiedValue<MapPoint>.Live(point, at)
            : ClassifiedValue<MapPoint>.Unknown("player_position_not_on_wire"),
        Inventory: inventory is null
            ? ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown("no_inventory_slot_observed")
            : ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Live(inventory, at));

    private static SelectableEntity Entity(long id, double? hp, DateTime at, int x = 105, int y = 100)
        => new(id, new MapPoint(x, y), hp, at);

    private static InventorySlotReading Slot(int slot, int vnum, int amount, DateTime at)
        => new(2, slot, vnum, amount, 0, at, DataSourceKind.Live);

    private static ActionCandidate Move(MapPoint to) => new(
        Guid.NewGuid(), ActionType.MoveToPosition, new ActionTarget.Position(to),
        0, TrustTier.Tier1_Assisted, "test");

    private static ActionCandidate Flee(MapPoint to) => new(
        Guid.NewGuid(), ActionType.EmergencyFlee, new ActionTarget.Position(to),
        0, TrustTier.Tier1_Assisted, "test");

    private static ActionCandidate Target(long entityId) => new(
        Guid.NewGuid(), ActionType.TargetEntity, new ActionTarget.Entity(entityId),
        0, TrustTier.Tier2_SemiAutonomous, "test");

    private static ActionCandidate Attack(long entityId) => new(
        Guid.NewGuid(), ActionType.UseBasicAttack, new ActionTarget.Entity(entityId),
        0, TrustTier.Tier2_SemiAutonomous, "test");

    private static ActionCandidate Skill(long entityId) => new(
        Guid.NewGuid(), ActionType.UseSkill, new ActionTarget.Entity(entityId),
        201, TrustTier.Tier2_SemiAutonomous, "test");

    private static ActionCandidate Consumable(int slot) => new(
        Guid.NewGuid(), ActionType.UseConsumable, new ActionTarget.InventorySlot(slot),
        101, TrustTier.Tier1_Assisted, "test");

    private static ActionCandidate Collect(MapPoint at) => new(
        Guid.NewGuid(), ActionType.CollectGroundItem, new ActionTarget.Position(at),
        0, TrustTier.Tier1_Assisted, "test");
}
