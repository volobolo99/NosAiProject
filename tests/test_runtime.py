from app.core.contracts import ActionType, Goal, WorldState
from app.core.orchestrator import NosAiOrchestrator
from app.llm.llama_cpp import LLMConnectionError


class OfflineProvider:
    def decide(self, world_state, goal):
        raise LLMConnectionError("offline")


def test_fallback_is_deterministic_and_safe():
    result = NosAiOrchestrator(primary=OfflineProvider()).tick(
        WorldState(hp_ratio=1.0, target_id=101), Goal("combat")
    )
    assert result.provider == "RuleBasedDecisionProvider"
    assert result.decision.action is ActionType.ATTACK
    assert result.safety.allowed


def test_low_hp_retires():
    result = NosAiOrchestrator(primary=OfflineProvider()).tick(
        WorldState(hp_ratio=0.2, target_id=101), Goal("survive")
    )
    assert result.decision.action is ActionType.RETREAT
