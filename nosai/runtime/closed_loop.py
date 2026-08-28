"""Closed-loop adapter: observe -> orchestrate -> execute -> verify -> re-observe."""
from __future__ import annotations
from dataclasses import dataclass
from typing import Callable

from nosai.core.contracts import CandidateAction, Goal, WorldState
from nosai.core.orchestrator import NosAiOrchestrator, TickResult

@dataclass(frozen=True)
class ClosedLoopStep:
    index: int
    action: CandidateAction
    executed: bool
    verified: bool
    replanned: bool = False
    reason: str = ""

@dataclass(frozen=True)
class ClosedLoopResult:
    completed: bool
    reason: str
    steps: tuple[ClosedLoopStep, ...]
    final_state: WorldState

class ClosedLoopRuntime:
    """Deterministic domain loop with a strict observation boundary."""
    def __init__(self, orchestrator: NosAiOrchestrator, observe: Callable[[], WorldState], execute: Callable[[CandidateAction], object], verify: Callable[[CandidateAction, object, WorldState], bool], max_steps: int = 16, max_replans: int = 3) -> None:
        self.orchestrator, self.observe, self.execute, self.verify = orchestrator, observe, execute, verify
        self.max_steps, self.max_replans = max(1, max_steps), max(0, max_replans)

    def run(self, goal: Goal, candidates: Callable[[WorldState], list[CandidateAction]]) -> ClosedLoopResult:
        traces: list[ClosedLoopStep] = []
        state = self.observe()
        replans = 0
        for index in range(self.max_steps):
            available = candidates(state)
            if not available:
                return ClosedLoopResult(False, "no_candidate", tuple(traces), state)
            result: TickResult = self.orchestrator.tick(state, goal)
            if not result.safety_allowed:
                return ClosedLoopResult(False, "orchestrator_safety_block", tuple(traces), state)
            action = result.ranked_actions[0].action if result.ranked_actions else result.decision.action
            try:
                observed = self.execute(action)
                new_state = self.observe()
                verified = self.verify(action, observed, new_state)
                reason = "verified" if verified else "post_action_verification_failed"
            except Exception as exc:
                observed = None
                new_state = self.observe()
                verified = False
                reason = f"execution_error:{type(exc).__name__}"
            traces.append(ClosedLoopStep(index, action, observed is not None, verified, not verified, reason))
            state = new_state
            if verified:
                continue
            if replans >= self.max_replans:
                return ClosedLoopResult(False, "replan_budget_exhausted", tuple(traces), state)
            replans += 1
        return ClosedLoopResult(True, "step_budget_reached", tuple(traces), state)
