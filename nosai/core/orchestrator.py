"""Safety-first runtime orchestration with optional party coordination."""

from dataclasses import dataclass, field

from .contracts import CandidateAction, Decision, DecisionStatus, Goal, WorldState
from .safety import SafetyGate
from .world_model import WorldModel
from .coordinated_action_manager import CoordinatedActionManager


@dataclass(frozen=True)
class TickResult:
    decision: Decision
    safety_allowed: bool
    safety_reason: str
    coordinated_actions: tuple[CandidateAction, ...] = field(default_factory=tuple)


class NosAiOrchestrator:
    def __init__(
        self,
        provider,
        safety_gate: SafetyGate | None = None,
        action_manager: CoordinatedActionManager | None = None,
    ) -> None:
        self.provider = provider
        self.safety_gate = safety_gate or SafetyGate()
        self.action_manager = action_manager

    def tick(self, world_state: WorldState, goal: Goal, world_model: WorldModel | None = None) -> TickResult:
        decision = self.provider.decide(world_state, goal)
        safety = self.safety_gate.evaluate(decision.action, world_state)
        if not safety.allowed:
            blocked = Decision(
                action=self.safety_gate.enforce(decision.action, world_state),
                confidence=decision.confidence,
                reasoning=f"Blocked by SafetyGate: {safety.reason}",
                status=DecisionStatus.BLOCKED,
            )
            coordinated = tuple(self.action_manager.propose(world_model)) if self.action_manager and world_model else ()
            return TickResult(blocked, False, safety.reason, coordinated)

        approved = Decision(
            action=decision.action,
            confidence=decision.confidence,
            reasoning=decision.reasoning,
            status=DecisionStatus.APPROVED,
        )
        coordinated = tuple(self.action_manager.propose(world_model)) if self.action_manager and world_model else ()
        return TickResult(approved, True, safety.reason, coordinated)
