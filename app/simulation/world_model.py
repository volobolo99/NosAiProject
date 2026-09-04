"""Data-oriented NosTale world model foundation for offline tactical planning.

The model intentionally contains no live-client or input integration. It captures
entities and objectives that can be populated from verified game data later.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum

from app.simulation.tactical import Element


class ObjectiveType(str, Enum):
    DEFEAT = "defeat"
    SURVIVE = "survive"
    REACH = "reach"
    COLLECT = "collect"
    TIMESPACE = "timespace"
    RAID = "raid"
    QUEST = "quest"


@dataclass(frozen=True)
class SkillModel:
    id: str
    name: str
    damage: int = 0
    mana_cost: int = 0
    cooldown: float = 0.0
    cast_time: float = 0.0
    range: float = 0.0
    element: Element = Element.NONE
    accuracy: float = 1.0
    area_radius: float = 0.0
    charges: int | None = None


@dataclass(frozen=True)
class BuffModel:
    id: str
    duration: float
    attack_delta: int = 0
    defense_delta: int = 0
    resistance_delta: dict[Element, int] = field(default_factory=dict)
    damage_multiplier: float = 1.0


@dataclass(frozen=True)
class MonsterModel:
    id: str
    name: str
    level: int
    hp: int
    attack: int
    defense: int
    element: Element = Element.NONE
    resistance: dict[Element, int] = field(default_factory=dict)
    skills: tuple[str, ...] = ()
    aggressive: bool = True
    move_speed: float = 0.0
    xp: int = 0


@dataclass(frozen=True)
class MapNode:
    id: str
    name: str
    x: float = 0.0
    y: float = 0.0
    neighbors: tuple[str, ...] = ()
    hazards: tuple[str, ...] = ()
    monsters: tuple[str, ...] = ()


@dataclass(frozen=True)
class ObjectiveModel:
    id: str
    kind: ObjectiveType
    target_ids: tuple[str, ...] = ()
    time_limit: float | None = None
    required_progress: float = 1.0
    reward_value: float = 0.0
    failure_penalty: float = 0.0


@dataclass(frozen=True)
class WorldModel:
    """Immutable knowledge snapshot used by simulation/planning layers."""

    maps: dict[str, MapNode] = field(default_factory=dict)
    monsters: dict[str, MonsterModel] = field(default_factory=dict)
    skills: dict[str, SkillModel] = field(default_factory=dict)
    buffs: dict[str, BuffModel] = field(default_factory=dict)
    objectives: dict[str, ObjectiveModel] = field(default_factory=dict)
    version: str = "unverified"
    source: str = "manual"

    def validate(self) -> tuple[str, ...]:
        errors: list[str] = []
        for node in self.maps.values():
            for neighbor in node.neighbors:
                if neighbor not in self.maps:
                    errors.append(f"map:{node.id}:unknown-neighbor:{neighbor}")
            for monster in node.monsters:
                if monster not in self.monsters:
                    errors.append(f"map:{node.id}:unknown-monster:{monster}")
        for monster in self.monsters.values():
            for skill in monster.skills:
                if skill not in self.skills:
                    errors.append(f"monster:{monster.id}:unknown-skill:{skill}")
        for objective in self.objectives.values():
            for target in objective.target_ids:
                if target not in self.monsters and target not in self.maps:
                    errors.append(f"objective:{objective.id}:unknown-target:{target}")
        return tuple(errors)
