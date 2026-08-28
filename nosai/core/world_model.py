"""Canonical party-aware World Model for NosAi.

The model is observation/domain state only. It does not perform game I/O.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Dict, Optional

from nosai.core.contracts import WorldState
from nosai.party import PartnerEntity, PetEntity

@dataclass
class WorldModel:
    """Single in-memory view joining core WorldState with party entities."""
    state: WorldState
    partners: Dict[str, PartnerEntity] = field(default_factory=dict)
    pets: Dict[str, PetEntity] = field(default_factory=dict)

    def partner(self, partner_id: Optional[str]) -> Optional[PartnerEntity]:
        return self.partners.get(partner_id) if partner_id else None

    def pet(self, pet_id: Optional[str]) -> Optional[PetEntity]:
        return self.pets.get(pet_id) if pet_id else None

    def tick(self, dt: float) -> None:
        for entity in self.partners.values():
            entity.tick(dt)
        for entity in self.pets.values():
            entity.tick(dt)

    def snapshot(self) -> WorldState:
        """Return the canonical core state without exposing mutable party maps."""
        return self.state
