"""Coordinator for independent Pet and Partner actors.

The manager produces candidate actions only. SafetyGate remains the mandatory
boundary before execution.
"""
from dataclasses import dataclass
from typing import Dict, List
from .contracts import ActionType, CandidateAction, WorldState
from nosai.party import PetEntity, PartnerEntity, PetBehavior, PartnerBehavior

@dataclass(frozen=True)
class PartyDecision:
    actor_id: str
    action: CandidateAction
    reason: str
    confidence: float

class CoordinatedActionManager:
    def __init__(self, pets: Dict[str, PetEntity] | None = None, partners: Dict[str, PartnerEntity] | None = None):
        self.pets = pets or {}
        self.partners = partners or {}

    def tick(self, world: WorldState, owner_position: tuple[float,float] | None = None,
             threat_level: float = 0.0) -> List[PartyDecision]:
        owner = owner_position or world.position
        decisions: List[PartyDecision] = []
        for pet_id, pet in self.pets.items():
            # Party actors are domain planners; no direct client I/O is performed.
            distance = 0.0
            behavior = pet.choose_behavior(distance, threat_level)
            action_type = {
                PetBehavior.FOLLOW: ActionType.MOVE,
                PetBehavior.GUARD: ActionType.NOOP,
                PetBehavior.ASSIST: ActionType.ATTACK,
                PetBehavior.RETREAT: ActionType.MOVE,
                PetBehavior.REST: ActionType.RECOVER,
            }[behavior]
            params = {"behavior": behavior.value, "owner_position": owner}
            decisions.append(PartyDecision(pet_id, CandidateAction(action_type, world.target_id, params, pet_id), behavior.value, 0.8))
        for partner_id, partner in self.partners.items():
            behavior = partner.tactical_behavior()
            action_type = {
                PartnerBehavior.TEAM_SUPPORT: ActionType.SKILL,
                PartnerBehavior.DEFENSIVE_SELF: ActionType.SKILL,
                PartnerBehavior.RETREAT_SELF_HEAL: ActionType.RECOVER,
                PartnerBehavior.HESITATE_OR_RETREAT: ActionType.NOOP,
            }[behavior]
            target = world.target_id if action_type is ActionType.SKILL and behavior is PartnerBehavior.TEAM_SUPPORT else None
            params = {"behavior": behavior.value, "obey_probability": partner.obey_probability()}
            decisions.append(PartyDecision(partner_id, CandidateAction(action_type, target, params, partner_id), behavior.value, partner.obey_probability()))
        return decisions
