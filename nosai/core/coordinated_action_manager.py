"""Party-aware action coordination layer.

It proposes actions from Pet/Partner state but leaves approval to the existing
SafetyGate/Orchestrator pipeline. No game-client I/O is performed here.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import List

from nosai.core.contracts import CandidateAction, ActionType
from nosai.core.world_model import WorldModel
from nosai.party import PartnerBehavior, PetBehavior

@dataclass(frozen=True)
class CoordinatedActionManager:
    """Builds deterministic candidate actions for party entities."""

    def propose(self, world: WorldModel) -> List[CandidateAction]:
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
