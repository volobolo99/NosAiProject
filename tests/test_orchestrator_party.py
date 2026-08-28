from nosai.ai.rule_based import RuleBasedDecisionProvider
from nosai.core.contracts import Goal, WorldState
from nosai.core.orchestrator import NosAiOrchestrator
from nosai.core.world_model import WorldModel
from nosai.core.coordinated_action_manager import CoordinatedActionManager
from nosai.party import PartnerEntity, PetEntity


def test_orchestrator_integrates_party_candidates_after_safety():
    state = WorldState(hp=100, mp=100, target_id=7, target_hp=100, tick_id=1)
    world = WorldModel(state)
    world.partners["partner-1"] = PartnerEntity("partner-1", "P", morale=90, trust=90)
    world.pets["pet-1"] = PetEntity("pet-1", "Pet")

    result = NosAiOrchestrator(
        RuleBasedDecisionProvider(),
        action_manager=CoordinatedActionManager(),
    ).tick(state, Goal("combat"), world)

    assert result.safety_allowed is True
    assert result.decision.status.value == "APPROVED"
    assert any(a.actor_id == "partner-1" for a in result.coordinated_actions)
    assert any(a.actor_id == "pet-1" for a in result.coordinated_actions)


def test_orchestrator_preserves_safety_on_blocked_primary_action():
    state = WorldState(hp=100, mp=100, target_id=None, target_hp=None, tick_id=2)
    world = WorldModel(state)
    world.partners["partner-1"] = PartnerEntity("partner-1", "P", morale=90, trust=90)
    result = NosAiOrchestrator(
        RuleBasedDecisionProvider(),
        action_manager=CoordinatedActionManager(),
    ).tick(state, Goal("combat"), world)
    assert result.safety_allowed is True
    assert result.decision.status.value == "APPROVED"
