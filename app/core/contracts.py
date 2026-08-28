from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum
from typing import Protocol


class ActionType(StrEnum):
    IDLE = "IDLE"
    MOVE = "MOVE"
    ATTACK = "ATTACK"
    USE_SKILL = "USE_SKILL"
    USE_ITEM = "USE_ITEM"
    RETREAT = "RETREAT"


@dataclass(frozen=True, slots=True)
class WorldState:
    hp_ratio: float = 1.0
    mp_ratio: float = 1.0
    target_id: int | None = None
    target_distance: float = 0.0
    time_remaining_sec: int = 0


@dataclass(frozen=True, slots=True)
class Goal:
    name: str = "survive"
    priority: float = 1.0


@dataclass(frozen=True, slots=True)
class Decision:
    action: ActionType
    target_id: int | None = None
    confidence: float = 0.0
    reasoning: str = ""

    def __post_init__(self) -> None:
        if not 0.0 <= self.confidence <= 1.0:
            raise ValueError("confidence must be between 0 and 1")


class DecisionProvider(Protocol):
    def decide(self, world_state: WorldState, goal: Goal) -> Decision: ...


@dataclass(frozen=True, slots=True)
class SafetyResult:
    allowed: bool
    reason: str = ""


@dataclass(frozen=True, slots=True)
class TickResult:
    decision: Decision
    safety: SafetyResult
    provider: str
    metadata: dict[str, str] = field(default_factory=dict)
