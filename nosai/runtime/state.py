"""Versioned observation state without granting execution authority."""
from __future__ import annotations
from dataclasses import dataclass
from typing import Any
from uuid import uuid4
from nosai.core.contracts import WorldState, PerceptionWorldUpdate

@dataclass(frozen=True)
class VersionedWorldState:
    state: WorldState
    state_version: int
    parent_version: int | None
    observation_id: str
    source: str
    confidence: float

class WorldStateStore:
    def __init__(self, initial: WorldState):
        self._current = VersionedWorldState(initial, 0, None, str(uuid4()), "initial", 1.0)
        self._history = [self._current]

    @property
    def current(self) -> VersionedWorldState:
        return self._current

    def apply(self, update: PerceptionWorldUpdate) -> VersionedWorldState:
        old = self._current.state
        version = self._current.state_version + 1
        state = WorldState(
            hp=old.hp if update.hp is None else update.hp,
            mp=old.mp if update.mp is None else update.mp,
            position=old.position if update.position is None else update.position,
            target_id=old.target_id if update.target_id is None else update.target_id,
            target_hp=old.target_hp if update.target_hp is None else update.target_hp,
            tick_id=old.tick_id if update.tick_id is None else update.tick_id,
            max_hp=old.max_hp, max_mp=old.max_mp,
            party_ids=old.party_ids, pet_ids=old.pet_ids, partner_ids=old.partner_ids,
        )
        observation_id = update.observation_id or str(uuid4())
        self._current = VersionedWorldState(state, version, self._current.state_version, observation_id, update.source, max(0.0, min(1.0, update.confidence)))
        self._history.append(self._current)
        return self._current

    def history(self) -> tuple[VersionedWorldState, ...]:
        return tuple(self._history)
