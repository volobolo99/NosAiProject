"""Deterministic Play AI tactical engine.

The engine evaluates utility candidates but never executes an action directly.
Execution remains behind Guard AI and the Safety Gate.
"""
from __future__ import annotations
from dataclasses import dataclass
from enum import Enum
from typing import Optional
from nosai.core.contracts import ActionType, CandidateAction, WorldState

class TacticalState(str, Enum):
    IDLE="Idle"; COMBAT="Combat"; LOOTING="Looting"; RECOVERY="Recovery"; EVADING="Evading"

@dataclass(frozen=True)
class UtilityAction:
    action: CandidateAction
    score: float
    state: TacticalState

class PlayAiEngine:
    def __init__(self, recovery_threshold: float = 0.35):
        self.recovery_threshold = recovery_threshold
        self.current_state = TacticalState.IDLE

    def evaluate_tick(self, world: WorldState) -> Optional[UtilityAction]:
        if world.hp <= 0:
            self.current_state = TacticalState.RECOVERY
            return UtilityAction(CandidateAction(ActionType.RECOVER), 1.0, self.current_state)
        if world.hp < self.recovery_threshold:
            self.current_state = TacticalState.RECOVERY
            return UtilityAction(CandidateAction(ActionType.RECOVER), 0.95, self.current_state)
        if world.target_id is not None:
            self.current_state = TacticalState.COMBAT
            return UtilityAction(CandidateAction(ActionType.ATTACK, target_id=world.target_id), 0.85, self.current_state)
        self.current_state = TacticalState.IDLE
        return None
