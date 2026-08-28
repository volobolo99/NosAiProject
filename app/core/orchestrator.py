from app.ai.rule_based import RuleBasedDecisionProvider
from app.core.contracts import Decision, DecisionProvider, Goal, TickResult, WorldState
from app.core.safety_gate import SafetyGateChecker
from app.llm.llama_cpp import LLMConnectionError, LlamaCppDecisionProvider


class NosAiOrchestrator:
    def __init__(self, primary: DecisionProvider | None = None, fallback: DecisionProvider | None = None) -> None:
        self.primary = primary or LlamaCppDecisionProvider()
        self.fallback = fallback or RuleBasedDecisionProvider()
        self.safety = SafetyGateChecker()

    def tick(self, world_state: WorldState, goal: Goal) -> TickResult:
        provider_name = type(self.primary).__name__
        try:
            decision = self.primary.decide(world_state, goal)
        except LLMConnectionError:
            decision = self.fallback.decide(world_state, goal)
            provider_name = type(self.fallback).__name__

        safety = self.safety.evaluate(decision, world_state)
        if not safety.allowed:
            decision = Decision(action="IDLE", confidence=1.0, reasoning=f"Safety override: {safety.reason}")
        return TickResult(decision=decision, safety=safety, provider=provider_name)
