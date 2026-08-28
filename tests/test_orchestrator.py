from nosai.ai.rule_based import RuleBasedDecisionProvider
from nosai.core.contracts import ActionType, Goal, WorldState
from nosai.core.orchestrator import NosAiOrchestrator


def test_orchestrator_approves_safe_rule_decision():
    state = WorldState(hp=100, mp=50, target_id=4, target_hp=30, tick_id=1)
    result = NosAiOrchestrator(RuleBasedDecisionProvider()).tick(state, Goal("combat"))
    assert result.safety_allowed
    assert result.decision.action.action is ActionType.ATTACK
    assert result.decision.status.value == "APPROVED"


def test_orchestrator_blocks_unsafe_provider_output():
    class UnsafeProvider:
        def decide(self, world_state, goal):
            from nosai.core.contracts import CandidateAction, Decision
            return Decision(CandidateAction(ActionType.ATTACK, target_id=999), 1.0, "bad")

    state = WorldState(hp=100, mp=50, target_id=4, tick_id=1)
    result = NosAiOrchestrator(UnsafeProvider()).tick(state, Goal("combat"))
    assert not result.safety_allowed
    assert result.decision.action.action is ActionType.NOOP
    assert result.decision.status.value == "BLOCKED"
