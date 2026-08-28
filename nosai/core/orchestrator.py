"""Safety-first runtime orchestration."""

from dataclasses import dataclass

from .contracts import Decision, DecisionStatus, Goal, WorldState
from .safety import SafetyGate


@dataclass(frozen=True)
class TickResult:
    decision: Decision
    safety_allowed: bool
    safety_reason: str


class NosAiOrchestrator:
    def __init__(self, provider, safety_gate: SafetyGate | None = None) -> None:
        self.provider = provider
        self.safety_gate = safety_gate or SafetyGate()

    def tick(self, world_state: WorldState, goal: Goal) -> TickResult:
        decision = self.provider.decide(world_state, goal)
        safety = self.safety_gate.evaluate(decision.action, world_state)
        if not safety.allowed:
            blocked = Decision(
                action=self.safety_gate.enforce(decision.action, world_state),
                confidence=decision.confidence,
                reasoning=f"Blocked by SafetyGate: {safety.reason}",
                status=DecisionStatus.BLOCKED,
            )
            return TickResult(blocked, False, safety.reason)
        approved = Decision(
            action=decision.action,
            confidence=decision.confidence,
            reasoning=decision.reasoning,
            status=DecisionStatus.APPROVED,
        )
        return TickResult(approved, True, safety.reason)
