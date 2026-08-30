"""Deterministic tactical ranking for coordinated Player/Pet/Partner actions."""
from __future__ import annotations
from dataclasses import dataclass
from enum import IntEnum
from typing import Iterable
from .contracts import CandidateAction, WorldState, ActionType
from .simulation_policy import TacticalSimulationPolicy

CRITICAL_HP_RATIO = 0.35
# SafetyGate.evaluate constrains observed HP to 0..100, so absolute HP remains
# comparable when max HP was never observed and no ratio can be derived.
CRITICAL_HP_ABSOLUTE = 25.0

class ActionPriority(IntEnum):
    """Ordering class applied ahead of the heuristic score.

    Survival is a class rather than a weight so that retuning the combat
    heuristics can never make an actor at critical HP keep attacking.
    """
    NORMAL = 0
    CRITICAL_SURVIVAL = 1

@dataclass(frozen=True)
class RankedAction:
    action: CandidateAction
    score: float
    reasons: tuple[str, ...]
    priority: ActionPriority = ActionPriority.NORMAL

class TacticalActionRanker:
    """Ranks proposals using tactical heuristics plus deterministic lookahead."""
    def __init__(self, simulation_policy: TacticalSimulationPolicy | None = None) -> None:
        self.simulation_policy = simulation_policy or TacticalSimulationPolicy()

    def rank(self, actions: Iterable[CandidateAction], world: WorldState) -> list[RankedAction]:
        ranked: list[RankedAction] = []
        hp_ratio = world.hp_ratio()
        hp_is_critical = (
            hp_ratio < CRITICAL_HP_RATIO if hp_ratio is not None
            else world.hp <= CRITICAL_HP_ABSOLUTE
        )
        for action in actions:
            score = 0.0
            reasons: list[str] = []
            priority = ActionPriority.NORMAL
            if action.target_id is not None and action.target_id == world.target_id:
                score += 30.0; reasons.append("target-aligned")
            if action.action is ActionType.RECOVER:
                if hp_ratio is not None:
                    score += max(0.0, 50.0 * (1.0 - hp_ratio))
                    reasons.append("survival-priority")
                else:
                    score += 50.0 if hp_is_critical else 0.0
                    reasons.append("survival-priority-unknown-max-hp")
                if hp_is_critical:
                    priority = ActionPriority.CRITICAL_SURVIVAL
                    reasons.append("critical-hp-override")
            elif action.action is ActionType.ATTACK and world.target_id is not None:
                score += 25.0; reasons.append("combat-objective")
            elif action.action is ActionType.MOVE:
                score += 5.0; reasons.append("positioning")
            actor = action.actor_id or ""
            if actor.startswith("partner"):
                score += 8.0; reasons.append("partner-coordination")
            elif actor.startswith("pet"):
                score += 6.0; reasons.append("pet-assist")
            forecast = self.simulation_policy.evaluate(action, world)
            score += forecast.score
            reasons.append(f"lookahead:{forecast.rationale}")
            ranked.append(RankedAction(action, score, tuple(reasons), priority))
        return sorted(ranked, key=lambda item: (
            -item.priority, -item.score,
            item.action.actor_id or "", str(item.action.target_id or ""),
        ))

    def best(self, actions: Iterable[CandidateAction], world: WorldState) -> RankedAction | None:
        ranked = self.rank(actions, world)
        return ranked[0] if ranked else None
