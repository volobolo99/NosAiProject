"""NosMate Pet autonomous domain model.

Keeps Pet behavior independent from Partner cognition and from game I/O.
"""
from __future__ import annotations
from dataclasses import dataclass
from enum import Enum

class PetBehavior(str, Enum):
    FOLLOW = "FOLLOW"
    GUARD = "GUARD"
    ASSIST = "ASSIST"
    RETREAT = "RETREAT"
    REST = "REST"

@dataclass
class PetEntity:
    pet_id: str
    name: str
    max_hp: float = 100.0
    current_hp: float = 100.0
    hunger: float = 0.0
    energy: float = 100.0
    survival_threshold: float = 20.0
    follow_distance: float = 3.0

    def health_ratio(self) -> float:
        return self.current_hp / self.max_hp if self.max_hp > 0 else 0.0

    def choose_behavior(self, owner_distance: float, threat_level: float = 0.0) -> PetBehavior:
        if self.energy <= 5.0 or self.hunger >= 95.0:
            return PetBehavior.REST
        if self.health_ratio() * 100.0 < self.survival_threshold:
            return PetBehavior.RETREAT
        if threat_level > 0.0:
            return PetBehavior.ASSIST
        if owner_distance > self.follow_distance:
            return PetBehavior.FOLLOW
        return PetBehavior.GUARD

    def tick(self, dt: float) -> None:
        self.energy = max(0.0, self.energy - max(0.0, dt) * 0.02)
        self.hunger = min(100.0, self.hunger + max(0.0, dt) * 0.01)
