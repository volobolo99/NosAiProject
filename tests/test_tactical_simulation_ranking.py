from nosai.core.contracts import ActionType, CandidateAction, WorldState
from nosai.core.tactical_ranking import TacticalActionRanker


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
