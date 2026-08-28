"""NosMate Partner cognitive model.

Pure domain logic: no game-client I/O, Unity dependencies, or action dispatch.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from enum import Enum
from math import exp
from typing import List

class RelationshipTier(str, Enum):
    STRANGER = "STRANGER"
    ALLY = "ALLY"
    TRUSTED = "TRUSTED"
    CORE_PARTNER = "CORE_PARTNER"

class SkillRank(str, Enum):
    F = "F"; E = "E"; D = "D"; C = "C"; B = "B"; A = "A"; S = "S"

class PartnerBehavior(str, Enum):
    RETREAT_SELF_HEAL = "RETREAT_SELF_HEAL"
    TEAM_SUPPORT = "TEAM_SUPPORT"
    DEFENSIVE_SELF = "DEFENSIVE_SELF"
    HESITATE_OR_RETREAT = "HESITATE_OR_RETREAT"

@dataclass
class PartnerSkill:
    skill_id: str
    name: str
    rank: SkillRank = SkillRank.C
    cooldown: float = 10.0
    remaining_cooldown: float = 0.0

    @property
    def ready(self) -> bool:
        return self.remaining_cooldown <= 0.0

    def tick(self, dt: float) -> None:
        self.remaining_cooldown = max(0.0, self.remaining_cooldown - max(0.0, dt))

@dataclass
class SpecialistPartnerCard:
    sp_id: str
    name: str
    element: str
    equipped: bool = False
    skills: List[PartnerSkill] = field(default_factory=list)

@dataclass
class MemoryEvent:
    impact: float
    description: str
    age_seconds: float = 0.0

@dataclass
class PartnerEntity:
    partner_id: str
    name: str
    max_hp: float = 100.0
    current_hp: float = 100.0
    morale: float = 80.0
    trust: float = 75.0
    affection: float = 60.0
    alpha_trust: float = 0.6
    battle_stress: float = 0.0
    active_sp: SpecialistPartnerCard | None = None
    short_term_memory: List[MemoryEvent] = field(default_factory=list)
    long_term_traits: List[str] = field(default_factory=list)

    def affinity(self) -> float:
        alpha = min(1.0, max(0.0, self.alpha_trust))
        return max(0.0, min(100.0, alpha * self.trust + (1.0 - alpha) * self.affection))

    def relationship_tier(self) -> RelationshipTier:
        value = self.affinity()
        if value >= 81: return RelationshipTier.CORE_PARTNER
        if value >= 51: return RelationshipTier.TRUSTED
        if value >= 21: return RelationshipTier.ALLY
        return RelationshipTier.STRANGER

    def decision_weight(self) -> float:
        return max(0.0, min(100.0, self.morale * 0.6 + self.trust * 0.4))

    def tactical_behavior(self) -> PartnerBehavior:
        hp_ratio = self.current_hp / self.max_hp if self.max_hp > 0 else 0.0
        if hp_ratio < 0.20 or self.morale < 15:
            return PartnerBehavior.RETREAT_SELF_HEAL
        weight = self.decision_weight()
        if weight >= 60: return PartnerBehavior.TEAM_SUPPORT
        if weight >= 40: return PartnerBehavior.DEFENSIVE_SELF
        return PartnerBehavior.HESITATE_OR_RETREAT

    def obey_probability(self) -> float:
        stress_factor = max(0.0, 1.0 - self.battle_stress / 200.0)
        return max(0.0, min(1.0, self.affinity() / 100.0 * self.morale / 100.0 * stress_factor))

    def register_memory(self, impact: float, description: str, consolidation_threshold: float = 30.0) -> None:
        self.short_term_memory.append(MemoryEvent(impact, description))
        if abs(impact) >= consolidation_threshold:
            self.trust = max(0.0, min(100.0, self.trust + impact))
            if description not in self.long_term_traits:
                self.long_term_traits.append(description)

    def decay_memory(self, dt: float, decay_lambda: float = 0.05) -> None:
        for event in self.short_term_memory:
            event.age_seconds += max(0.0, dt)
            event.impact *= exp(-max(0.0, decay_lambda) * max(0.0, dt))
        self.short_term_memory = [e for e in self.short_term_memory if abs(e.impact) > 0.1]

    def tick(self, dt: float) -> None:
        if self.active_sp and self.active_sp.equipped:
            for skill in self.active_sp.skills:
                skill.tick(dt)
        self.decay_memory(dt)
