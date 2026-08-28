"""Adapter from semantic perception to the canonical WorldState."""
from __future__ import annotations
from .contracts import PerceptionSnapshot
from nosai.core.contracts import WorldState, PerceptionWorldUpdate

class PerceptionWorldAdapter:
    def update(self, world: WorldState, snapshot: PerceptionSnapshot) -> WorldState:
        target_id = self._target_id(snapshot)
        return WorldState(
            hp=self._value(snapshot.player_hp_pct, world.hp),
            mp=self._value(snapshot.player_mp_pct, world.mp),
            position=world.position,
            target_id=target_id if target_id is not None else world.target_id,
            target_hp=self._value(snapshot.target_hp_pct, world.target_hp),
            tick_id=max(world.tick_id, int(snapshot.timestamp)),
            party_ids=world.party_ids,
            pet_ids=world.pet_ids,
            partner_ids=world.partner_ids,
        )

    @staticmethod
    def _value(value, fallback):
        return fallback if value is None else value

    @staticmethod
    def _target_id(snapshot: PerceptionSnapshot):
        for entity in snapshot.entities:
            if entity.label in {"target", "enemy", "monster"}:
                try:
                    return int(entity.label.split(":", 1)[1]) if ":" in entity.label else None
                except ValueError:
                    return None
        return None
