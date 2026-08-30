from nosai.core.contracts import ActionType, CandidateAction, WorldState
from nosai.core.simulation_policy import TacticalSimulationPolicy
from nosai.core.tactical_ranking import ActionPriority, TacticalActionRanker


def test_lookahead_contributes_to_ranking():
    world = WorldState(hp=100, mp=100, target_id=1, target_hp=100)
    actions = [
        CandidateAction(ActionType.ATTACK, target_id=1, actor_id="partner-1"),
        CandidateAction(ActionType.MOVE, actor_id="pet-1"),
    ]
    ranked = TacticalActionRanker().rank(actions, world)
    assert ranked[0].action.action is ActionType.ATTACK
    assert any(reason.startswith("lookahead:") for reason in ranked[0].reasons)


def test_low_hp_recovery_forecast_is_prioritized():
    world = WorldState(hp=10, mp=100, target_id=1, target_hp=100)
    actions = [
        CandidateAction(ActionType.RECOVER, actor_id="partner-1"),
        CandidateAction(ActionType.ATTACK, target_id=1, actor_id="pet-1"),
    ]
    ranked = TacticalActionRanker().rank(actions, world)
    assert ranked[0].action.action is ActionType.RECOVER


def test_attack_forecast_tolerates_unknown_target_hp():
    world = WorldState(hp=100, max_hp=100, mp=100, target_id=1, target_hp=None)
    outcome = TacticalSimulationPolicy().evaluate(
        CandidateAction(ActionType.ATTACK, target_id=1), world
    )
    assert "expected-combat-progress-unknown-target-hp" in outcome.rationale
    assert outcome.score == 35.0


def test_unknown_max_hp_does_not_fabricate_a_survival_score():
    world = WorldState(hp=100, mp=100, target_id=1, target_hp=100)
    ranked = TacticalActionRanker().rank([CandidateAction(ActionType.RECOVER)], world)
    # No ratio can be derived, so no survival weight is invented; the state is
    # reported as UNKNOWN rather than silently ranked as healthy.
    assert ranked[0].score == 0.0
    assert ranked[0].priority is ActionPriority.UNKNOWN_SURVIVAL
    assert "survival-priority-unknown-max-hp" in ranked[0].reasons
    assert "critical-hp-override" not in ranked[0].reasons


def test_unknown_max_hp_still_ranks_recovery_over_combat():
    """Regression: absolute HP carries no scale before the Safety stage.

    An actor at 200/6000 HP is dying, but 200 is far above any absolute
    threshold calibrated for a 0..100 bar. Ranking must fail closed instead of
    guessing the scale.
    """
    world = WorldState(hp=200, mp=50, target_id=1, target_hp=100)
    actions = [
        CandidateAction(ActionType.ATTACK, target_id=1, actor_id="pet-1"),
        CandidateAction(ActionType.RECOVER, actor_id="partner-1"),
    ]
    ranked = TacticalActionRanker().rank(actions, world)
    assert ranked[0].action.action is ActionType.RECOVER
    assert ranked[0].priority is ActionPriority.UNKNOWN_SURVIVAL
    # Ordering comes from the priority class, not from an inflated score.
    assert ranked[0].score < ranked[1].score


def test_known_healthy_hp_does_not_preempt_combat():
    world = WorldState(hp=95, max_hp=100, mp=100, target_id=1, target_hp=100)
    actions = [
        CandidateAction(ActionType.ATTACK, target_id=1, actor_id="pet-1"),
        CandidateAction(ActionType.RECOVER, actor_id="partner-1"),
    ]
    ranked = TacticalActionRanker().rank(actions, world)
    assert ranked[0].action.action is ActionType.ATTACK
    assert all(item.priority is ActionPriority.NORMAL for item in ranked)


def test_critical_hp_outranks_combat_regardless_of_score():
    world = WorldState(hp=10, max_hp=100, mp=100, target_id=1, target_hp=100)
    actions = [
        CandidateAction(ActionType.ATTACK, target_id=1, actor_id="pet-1"),
        CandidateAction(ActionType.RECOVER, actor_id="partner-1"),
    ]
    ranked = TacticalActionRanker().rank(actions, world)
    assert ranked[0].action.action is ActionType.RECOVER
    assert ranked[0].priority is ActionPriority.CRITICAL_SURVIVAL
    assert ranked[0].score < ranked[1].score
