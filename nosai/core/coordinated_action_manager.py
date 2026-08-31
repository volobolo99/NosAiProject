"""Party-aware action coordination layer.

It proposes actions from Pet/Partner state but leaves approval to the existing
SafetyGate/Orchestrator pipeline. No game-client I/O is performed here.

This module absorbs what used to live in a second, never-imported
``nosai/core/party_manager.py`` that declared its own ``CoordinatedActionManager``.
Both entry points survive the merge because they answer different questions:

* :meth:`CoordinatedActionManager.propose` reads the live :class:`WorldModel` and
  returns bare candidates. It is what the Orchestrator calls.
* :meth:`CoordinatedActionManager.decide` works from actors handed to the
  constructor and returns :class:`PartyDecision`, which carries the *reason* and a
  *confidence* the bare candidate cannot express.

**They do not agree on every mapping, and that disagreement is deliberate rather
than an oversight** — see :data:`PET_ACTION_BY_BEHAVIOR`. Reconciling them changes
what the party actually does in game, which is a gameplay decision, not a
refactor; until it is made, both readings stay visible instead of one being
silently deleted.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple

from nosai.core.contracts import CandidateAction, ActionType, WorldState
from nosai.core.world_model import WorldModel
from nosai.party import PartnerBehavior, PetBehavior, PetEntity, PartnerEntity


@dataclass(frozen=True)
class PartyDecision:
    """A proposed party action together with why it was proposed.

    The reason and confidence are the point: a bare
    :class:`~nosai.core.contracts.CandidateAction` records what to do but not how
    sure the planner is, so nothing downstream can weigh it or explain it.
    """

    actor_id: str
    action: CandidateAction
    reason: str
    confidence: float


# Exhaustive on purpose. A dict lookup raises KeyError when a behaviour is added
# without deciding what it should do, which is the loud failure the ``propose``
# path lacks — there, an unmapped behaviour silently yields no candidate at all.
#
# Note the disagreement with ``propose`` kept from the two merged sources:
#   RETREAT   -> MOVE here, RECOVER in propose
#   ASSIST    -> ATTACK in both
# and for partners:
#   TEAM_SUPPORT -> SKILL here, ATTACK in propose
PET_ACTION_BY_BEHAVIOR: Dict[PetBehavior, ActionType] = {
    PetBehavior.FOLLOW: ActionType.MOVE,
    PetBehavior.GUARD: ActionType.NOOP,
    PetBehavior.ASSIST: ActionType.ATTACK,
    PetBehavior.RETREAT: ActionType.MOVE,
    PetBehavior.REST: ActionType.RECOVER,
}

PARTNER_ACTION_BY_BEHAVIOR: Dict[PartnerBehavior, ActionType] = {
    PartnerBehavior.TEAM_SUPPORT: ActionType.SKILL,
    PartnerBehavior.DEFENSIVE_SELF: ActionType.SKILL,
    PartnerBehavior.RETREAT_SELF_HEAL: ActionType.RECOVER,
    PartnerBehavior.HESITATE_OR_RETREAT: ActionType.NOOP,
}


class CoordinatedActionManager:
    """Builds deterministic candidate actions for party entities."""

    def __init__(
        self,
        pets: Optional[Dict[str, PetEntity]] = None,
        partners: Optional[Dict[str, PartnerEntity]] = None,
    ):
        # Both default to empty so ``CoordinatedActionManager()`` keeps working for
        # the Orchestrator, which supplies the actors through the world instead.
        self.pets = pets or {}
        self.partners = partners or {}

    # ------------------------------------------------------------ world-driven

    def propose(self, world: WorldModel) -> List[CandidateAction]:
        """Candidate actions for the party as the world currently reports it.

        Behaviours with no branch here yield no candidate. That is why
        :meth:`decide` exists with an exhaustive table.
        """
        candidates: List[CandidateAction] = []
        state = world.state
        for partner in world.partners.values():
            behavior = partner.tactical_behavior()
            if behavior is PartnerBehavior.RETREAT_SELF_HEAL:
                candidates.append(CandidateAction(ActionType.RECOVER, actor_id=partner.partner_id))
            elif behavior is PartnerBehavior.TEAM_SUPPORT and state.target_id:
                candidates.append(CandidateAction(ActionType.ATTACK, actor_id=partner.partner_id, target_id=state.target_id))
            elif behavior is PartnerBehavior.DEFENSIVE_SELF:
                candidates.append(CandidateAction(ActionType.RECOVER, actor_id=partner.partner_id))

        owner_position = state.position
        for pet in world.pets.values():
            behavior = pet.choose_behavior(owner_distance=0.0, threat_level=1.0 if state.target_id else 0.0)
            if behavior is PetBehavior.RETREAT:
                candidates.append(CandidateAction(ActionType.RECOVER, actor_id=pet.pet_id))
            elif behavior is PetBehavior.ASSIST and state.target_id:
                candidates.append(CandidateAction(ActionType.ATTACK, actor_id=pet.pet_id, target_id=state.target_id))
            elif behavior is PetBehavior.FOLLOW:
                candidates.append(CandidateAction(ActionType.MOVE, actor_id=pet.pet_id))
        return candidates

    # ---------------------------------------------------------- actor-driven

    def decide(
        self,
        world: WorldState,
        owner_position: Optional[Tuple[float, float]] = None,
        threat_level: float = 0.0,
    ) -> List[PartyDecision]:
        """Reasoned decisions for the actors held by this manager.

        Every behaviour maps to exactly one action, so a pet or partner is never
        quietly left out of a tick.
        """
        owner = owner_position or world.position
        decisions: List[PartyDecision] = []

        for pet_id, pet in self.pets.items():
            # Party actors are domain planners; no direct client I/O is performed.
            behavior = pet.choose_behavior(0.0, threat_level)
            action_type = PET_ACTION_BY_BEHAVIOR[behavior]
            params = {"behavior": behavior.value, "owner_position": owner}
            decisions.append(
                PartyDecision(
                    pet_id,
                    CandidateAction(action_type, world.target_id, params, pet_id),
                    behavior.value,
                    0.8,
                )
            )

        for partner_id, partner in self.partners.items():
            behavior = partner.tactical_behavior()
            action_type = PARTNER_ACTION_BY_BEHAVIOR[behavior]
            target = (
                world.target_id
                if action_type is ActionType.SKILL and behavior is PartnerBehavior.TEAM_SUPPORT
                else None
            )
            params = {"behavior": behavior.value, "obey_probability": partner.obey_probability()}
            decisions.append(
                PartyDecision(
                    partner_id,
                    CandidateAction(action_type, target, params, partner_id),
                    behavior.value,
                    partner.obey_probability(),
                )
            )
        return decisions

    # The name this logic carried in the merged module. Kept so the older call
    # shape still resolves rather than failing at import time somewhere unseen.
    tick = decide
