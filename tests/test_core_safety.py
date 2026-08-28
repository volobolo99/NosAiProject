from nosai.core.contracts import ActionType, CandidateAction, Goal, WorldState
from nosai.core.safety import SafetyGate


def test_safe_target_action_is_allowed():
    state = WorldState(hp=100, mp=50, target_id=10)
    action = CandidateAction(ActionType.ATTACK, target_id=10)
    result = SafetyGate().evaluate(action, state)
    assert result.allowed


def test_wrong_target_is_blocked():
    state = WorldState(hp=100, mp=50, target_id=10)
    action = CandidateAction(ActionType.ATTACK, target_id=11)
    result = SafetyGate().evaluate(action, state)
    assert not result.allowed
    assert SafetyGate().enforce(action, state).action is ActionType.NOOP


def test_target_action_without_target_is_blocked():
    state = WorldState(hp=100, mp=50)
    action = CandidateAction(ActionType.SKILL)
    assert not SafetyGate().evaluate(action, state).allowed


def test_invalid_observation_is_blocked():
    state = WorldState(hp=101, mp=50)
    action = CandidateAction(ActionType.MOVE)
    assert not SafetyGate().evaluate(action, state).allowed


def test_noop_is_always_safe():
    state = WorldState(hp=100, mp=50)
    assert SafetyGate().evaluate(CandidateAction(ActionType.NOOP), state).allowed
