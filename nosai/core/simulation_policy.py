"""Deterministic one-step lookahead policy for tactical ranking."""
from __future__ import annotations
from dataclasses import dataclass
from .contracts import CandidateAction, ActionType, WorldState

@dataclass(frozen=True)
class SimulationOutcome:
    action: CandidateAction
    score: float
    rationale: str

class TacticalSimulationPolicy:
    """Cheap deterministic forecast used by the ranker; never executes actions."""
    def evaluate(self, action: CandidateAction, world: WorldState) -> SimulationOutcome:
        score = 0.0
        rationale = []
        if action.target_id == world.target_id and world.target_id is not None:
            score += 35.0; rationale.append("target-preserved")
        if action.action is ActionType.ATTACK:
            if world.target_hp is None:
                rationale.append("expected-combat-progress-unknown-target-hp")
            else:
                score += 30.0 if world.target_hp > 0 else 0.0
                rationale.append("expected-combat-progress")
        elif action.action is ActionType.RECOVER:
            hp_ratio = world.hp_ratio()
            if hp_ratio is None:
                rationale.append("expected-survivability-unknown-max-hp")
            else:
                score += max(0.0, 45.0 * (1.0 - hp_ratio))
                rationale.append("expected-survivability")
        elif action.action is ActionType.MOVE:
            score += 8.0
            rationale.append("expected-positioning")
        return SimulationOutcome(action, score, ";".join(rationale) or "neutral-forecast")
