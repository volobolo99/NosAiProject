"""Combat action vocabulary shared by the three predictive tiers.

Damage and healing are fractions of the relevant HP bar, never absolute points.
The repository has already been bitten once by absolute HP carrying no scale
(see ``ActionPriority.UNKNOWN_SURVIVAL`` in ``nosai.core.tactical_ranking``):
200 HP is lethal on a 6000 HP bar and healthy on a 250 HP one. Ratios are the
only unit under which one tuning constant stays meaningful for every character.

An effect's ``nominal_probability`` is what the client *claims*. It seeds a
Bayesian prior and is never counted as an observation; the measured value lives
in :mod:`nosai.tactical.stochastic`.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Iterator, Mapping

from nosai.core.contracts import ActionType

# Action types this engine may propose. PICKUP is excluded deliberately: looting
# is not a combat decision and routing it through the combat value function
# would let loot outrank survival.
COMBAT_ACTION_TYPES = frozenset(
    {ActionType.ATTACK, ActionType.SKILL, ActionType.RECOVER, ActionType.MOVE, ActionType.NOOP}
)


@dataclass(frozen=True)
class EffectSpec:
    """A status effect an action claims to apply to its target.

    Both scales are multipliers applied *while the effect is active*, so a stun
    is ``incoming_damage_scale=0.0`` (the target deals nothing), a slow might be
    ``0.6``, and a vulnerability debuff is ``outgoing_damage_scale>1.0``.
    Composition across simultaneous effects is multiplicative.
    """

    effect_id: str
    nominal_probability: float
    duration_s: float
    incoming_damage_scale: float = 1.0
    outgoing_damage_scale: float = 1.0

    def __post_init__(self) -> None:
        if not self.effect_id:
            raise ValueError("effect_id must be non-empty")
        if not 0.0 <= self.nominal_probability <= 1.0:
            raise ValueError(f"{self.effect_id}: nominal_probability must lie in [0, 1]")
        if self.duration_s < 0.0:
            raise ValueError(f"{self.effect_id}: duration_s must be non-negative")
        if self.incoming_damage_scale < 0.0 or self.outgoing_damage_scale < 0.0:
            raise ValueError(f"{self.effect_id}: damage scales must be non-negative")


@dataclass(frozen=True)
class ActionSpec:
    """Timing, cost and claimed yield of one action.

    ``cast_s`` and ``animation_lock_s`` are separate because they fail
    differently. Interrupting a cast destroys the payload; interrupting the
    animation lock does not, because the payload already landed. Tier A relies
    on that asymmetry to decide whether a cancel is free or expensive.
    """

    action_id: str
    action_type: ActionType
    cooldown_s: float
    cast_s: float = 0.0
    animation_lock_s: float = 0.0
    mp_ratio_cost: float = 0.0
    damage_ratio: float = 0.0
    heal_ratio: float = 0.0
    effect: EffectSpec | None = None
    cancellable: bool = True
    consumes_cooldown_on_interrupt: bool = True

    def __post_init__(self) -> None:
        if not self.action_id:
            raise ValueError("action_id must be non-empty")
        if self.action_type not in COMBAT_ACTION_TYPES:
            raise ValueError(f"{self.action_id}: {self.action_type} is not a combat action type")
        for name in ("cooldown_s", "cast_s", "animation_lock_s"):
            if getattr(self, name) < 0.0:
                raise ValueError(f"{self.action_id}: {name} must be non-negative")
        for name in ("mp_ratio_cost", "damage_ratio", "heal_ratio"):
            value = getattr(self, name)
            if not 0.0 <= value <= 1.0:
                raise ValueError(f"{self.action_id}: {name} must lie in [0, 1] (bar fraction)")

    @property
    def occupancy_s(self) -> float:
        """Wall time the action removes from the decision loop."""
        return self.cast_s + self.animation_lock_s

    @property
    def is_survival(self) -> bool:
        """Whether failing to attempt this action is more costly than attempting it.

        Tier A treats survival actions differently under an unknown cooldown:
        see ``ShadowCooldownClock`` and the asymmetry documented there.
        """
        return self.action_type is ActionType.RECOVER or self.heal_ratio > 0.0


class ActionBook:
    """Immutable, validated registry of the actions the engine may propose.

    Lookup of an unregistered id raises rather than returning a default. A
    silently missing action would be indistinguishable from an action that is
    permanently on cooldown, and the engine would simply stop using it.
    """

    def __init__(self, specs: tuple[ActionSpec, ...] | list[ActionSpec]) -> None:
        by_id: dict[str, ActionSpec] = {}
        for spec in specs:
            if spec.action_id in by_id:
                raise ValueError(f"duplicate action_id: {spec.action_id}")
            by_id[spec.action_id] = spec
        # Sorted so every downstream iteration order — search expansion, tie
        # breaking, reported diagnostics — is reproducible across runs.
        self._by_id: Mapping[str, ActionSpec] = dict(sorted(by_id.items()))

    def __getitem__(self, action_id: str) -> ActionSpec:
        return self._by_id[action_id]

    def __contains__(self, action_id: object) -> bool:
        return action_id in self._by_id

    def __iter__(self) -> Iterator[ActionSpec]:
        return iter(self._by_id.values())

    def __len__(self) -> int:
        return len(self._by_id)

    @property
    def ids(self) -> tuple[str, ...]:
        return tuple(self._by_id)

    def get(self, action_id: str) -> ActionSpec | None:
        return self._by_id.get(action_id)
