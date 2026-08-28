"""Deterministic tactical ranking for coordinated Player/Pet/Partner actions."""
from __future__ import annotations
from dataclasses import dataclass
from typing import Iterable
from .contracts import CandidateAction, WorldState, ActionType

@dataclass(frozen=True)
class RankedAction:
    action: CandidateAction
    score: float
    reasons: tuple[str, ...]

class TacticalActionRanker:
    """Ranks proposals without executing them or bypassing SafetyGate."""
    def rank(self, actions: Iterable[CandidateAction], world: WorldState) -> list[RankedAction]:
        ranked=[]
        for action in actions:
            score=0.0; reasons=[]
            if action.target_id and action.target_id == world.target_id:
                score += 30; reasons.append("target-aligned")
            if action.action_type is ActionType.RECOVER:
                hp_ratio = world.hp / world.max_hp if world.max_hp else 0.0
                score += max(0.0, 50.0 * (1.0 - hp_ratio))
                reasons.append("survival-priority")
            elif action.action_type is ActionType.ATTACK and world.target_id:
                score += 25; reasons.append("combat-objective")
            elif action.action_type is ActionType.MOVE:
                score += 5; reasons.append("positioning")
            actor = action.actor_id or ""
            if actor.startswith("partner"):
                score += 8; reasons.append("partner-coordination")
            elif actor.startswith("pet"):
                score += 6; reasons.append("pet-assist")
            score += max(0.0, min(1.0, action.confidence)) * 10.0
            ranked.append(RankedAction(action, score, tuple(reasons)))
        return sorted(ranked, key=lambda item: (-item.score, item.action.actor_id or "", item.action.target_id or ""))

    def best(self, actions: Iterable[CandidateAction], world: WorldState) -> RankedAction | None:
        ranked=self.rank(actions, world)
        return ranked[0] if ranked else None
