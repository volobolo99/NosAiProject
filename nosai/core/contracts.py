"""Core domain contracts for NosAi.

These contracts are intentionally small and deterministic. They define the
boundary between observation, decision, safety and execution.
"""

from dataclasses import dataclass, field
from enum import Enum
from typing import Optional, Protocol, Sequence


class ActionType(str, Enum):
    NOOP = "NOOP"
    MOVE = "MOVE"
    ATTACK = "ATTACK"
    SKILL = "SKILL"
    PICKUP = "PICKUP"
    RECOVER = "RECOVER"


class DecisionStatus(str, Enum):
    PROPOSED = "PROPOSED"
    APPROVED = "APPROVED"
    BLOCKED = "BLOCKED"


@dataclass(frozen=True)
class WorldState:
    """Minimal read-only state supplied to the decision layer."""

    hp: float
    mp: float
    position: tuple[float, float] = (0.0, 0.0)
    target_id: Optional[int] = None
    target_hp: Optional[float] = None
    tick_id: int = 0


@dataclass(frozen=True)
class Goal:
    name: str
    priority: int = 0


@dataclass(frozen=True)
class CandidateAction:
    action: ActionType
    target_id: Optional[int] = None
    parameters: dict[str, object] = field(default_factory=dict)


@dataclass(frozen=True)
class Decision:
    action: CandidateAction
    confidence: float
    reasoning: str = ""
    status: DecisionStatus = DecisionStatus.PROPOSED


@dataclass(frozen=True)
class SafetyResult:
    allowed: bool
    reason: str


class DecisionProvider(Protocol):
    def decide(self, world_state: WorldState, goal: Goal) -> Decision:
        ...


class ActionExecutor(Protocol):
    def execute(self, action: CandidateAction) -> object:
        ...
