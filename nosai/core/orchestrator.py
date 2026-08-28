"""Safety-first runtime orchestration with party coordination and ranking."""

from dataclasses import dataclass, field

from .contracts import CandidateAction, Decision, DecisionStatus, Goal, WorldState
from .safety import SafetyGate
from .world_model import WorldModel
from .coordinated_action_manager import CoordinatedActionManager
from .tactical_ranking import TacticalActionRanker, RankedAction


@dataclass(frozen=True)
class TickResult:
    decision: Decision
    safety_allowed: bool
    safety_reason: str
    coordinated_actions: tuple[CandidateAction, ...] = field(default_factory=tuple)
    ranked_actions: tuple[RankedAction, ...] = field(default_factory=tuple)
    selected_coordinated_action: RankedAction | None = None


class NosAiOrchestrator:
    def __init__(
        self,
        provider,
        safety_gate: SafetyGate | None = None,
        action_manager: CoordinatedActionManager | None = None,
        action_ranker: TacticalActionRanker | None = None,
    ) -> None:
        self.provider = provider
        self.safety_gate = safety_gate or SafetyGate()
        self.action_manager = action_manager
        self.action_ranker = action_ranker or TacticalActionRanker()

    def tick(self, world_state: WorldState, goal: Goal, world_model: WorldModel | None = None) -> TickResult:
        decision = self.provider.decide(world_state, goal)
        safety = self.safety_gate.evaluate(decision.action, world_state)
        if not safety.allowed:
            final_decision = Decision(
                action=self.safety_gate.enforce(decision.action, world_state),
                confidence=decision.confidence,
                reasoning=f"Blocked by SafetyGate: {safety.reason}",
                status=DecisionStatus.BLOCKED,
            )
            return self._result(final_decision, False, safety.reason, world_state, world_model)

        approved = Decision(
            action=decision.action,
            confidence=decision.confidence,
            reasoning=decision.reasoning,
            status=DecisionStatus.APPROVED,
        )
        return self._result(approved, True, safety.reason, world_state, world_model)

    def _result(
        self,
        decision: Decision,
        allowed: bool,
        reason: str,
        world_state: WorldState,
        world_model: WorldModel | None,
    ) -> TickResult:
        coordinated = tuple(
            self.action_manager.propose(world_model)
            if self.action_manager and world_model else ()
        )
        ranked = tuple(self.action_ranker.rank(coordinated, world_state))
        selected = ranked[0] if ranked else None
        return TickResult(
            decision=decision,
            safety_allowed=allowed,
            safety_reason=reason,
            coordinated_actions=coordinated,
            ranked_actions=ranked,
            selected_coordinated_action=selected,
        )
