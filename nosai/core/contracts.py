"""Core domain contracts for NosAi."""
from dataclasses import dataclass, field
from enum import Enum
from typing import Optional, Protocol

class ActionType(str, Enum):
    NOOP="NOOP"; MOVE="MOVE"; ATTACK="ATTACK"; SKILL="SKILL"; PICKUP="PICKUP"; RECOVER="RECOVER"
class DecisionStatus(str, Enum):
    PROPOSED="PROPOSED"; APPROVED="APPROVED"; BLOCKED="BLOCKED"

@dataclass(frozen=True)
class Position:
    x: float
    y: float

@dataclass(frozen=True)
class WorldState:
    hp: float
    mp: float
    position: Position | tuple[float,float]=(0.0,0.0)
    target_id: Optional[object]=None
    target_hp: Optional[float]=None
    tick_id: int=0
    max_hp: Optional[float]=None
    max_mp: Optional[float]=None
    party_ids: tuple[str,...]=()
    pet_ids: tuple[str,...]=()
    partner_ids: tuple[str,...]=()

    def hp_ratio(self) -> Optional[float]:
        """Observed HP as a fraction of max HP, or None when max HP is unknown.

        Callers must handle None explicitly: an unobserved max HP is not a full
        bar and not an empty one, so no ratio may be substituted for it.
        """
        if self.max_hp is None or self.max_hp <= 0:
            return None
        return self.hp / self.max_hp
@dataclass(frozen=True)
class Goal:
    name: str
    priority: int=0
@dataclass(frozen=True)
class CandidateAction:
    action: ActionType
    target_id: Optional[object]=None
    parameters: dict[str,object]=field(default_factory=dict)
    actor_id: Optional[str]=None
@dataclass(frozen=True)
class Decision:
    action: CandidateAction
    confidence: float
    reasoning: str=""
    status: DecisionStatus=DecisionStatus.PROPOSED
@dataclass(frozen=True)
class SafetyResult:
    allowed: bool
    reason: str
@dataclass(frozen=True)
class PerceptionWorldUpdate:
    """Validated semantic observation mapped into WorldState fields."""
    hp: Optional[float]=None
    mp: Optional[float]=None
    target_hp: Optional[float]=None
    target_id: Optional[object]=None
    position: Optional[Position | tuple[float,float]]=None
    tick_id: Optional[int]=None
class DecisionProvider(Protocol):
    def decide(self, world_state: WorldState, goal: Goal) -> Decision: ...
class ActionExecutor(Protocol):
    def execute(self, action: CandidateAction) -> object: ...
