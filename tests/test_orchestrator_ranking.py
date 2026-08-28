from nosai.core.coordinated_action_manager import CoordinatedActionManager
from nosai.core.orchestrator import NosAiOrchestrator
from nosai.core.tactical_ranking import TacticalActionRanker
from nosai.core.world_model import WorldModel
from nosai.core.contracts import Goal, WorldState, Position
from nosai.party import PartnerEntity, PetEntity
from nosai.ai.rule_based import RuleBasedDecisionProvider


def test_orchestrator_ranks_party_candidates():
    state = WorldState(
        tick_id=1, hp=100, max_hp=100, mp=100, max_mp=100,
        position=Position(0, 0), target_id="enemy-1", target_hp=100,
    )
    world = WorldModel(state)
    world.partners["partner-1"] = PartnerEntity("partner-1", "P", morale=90, trust=90)
    world.pets["pet-1"] = PetEntity("pet-1", "Pet", current_hp=100)

    result = NosAiOrchestrator(
        RuleBasedDecisionProvider(),
        action_manager=CoordinatedActionManager(),
        action_ranker=TacticalActionRanker(),
    ).tick(state, Goal("combat"), world)

    assert result.safety_allowed
    assert result.ranked_actions
    assert result.selected_coordinated_action is result.ranked_actions[0]
    assert result.selected_coordinated_action.action.target_id == "enemy-1"
