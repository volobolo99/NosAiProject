from nosai.core.coordinated_action_manager import CoordinatedActionManager
from nosai.core.contracts import WorldState, Position
from nosai.core.world_model import WorldModel
from nosai.party import PartnerEntity, PartnerBehavior, PetEntity, PetBehavior


def make_world(target_id="enemy-1"):
    state = WorldState(
        tick_id=1, hp=100, max_hp=100, mp=100, max_mp=100,
        position=Position(0, 0), target_id=target_id, target_hp=100,
    )
    return WorldModel(state)


def test_world_model_ticks_party_entities():
    world = make_world()
    partner = PartnerEntity("partner-1", "P", morale=80, trust=80)
    pet = PetEntity("pet-1", "Pet", energy=100, hunger=0)
    world.partners[partner.partner_id] = partner
    world.pets[pet.pet_id] = pet
    world.tick(10)
    assert pet.energy < 100
    assert partner.tactical_behavior() is PartnerBehavior.TEAM_SUPPORT


def test_coordinated_manager_proposes_party_actions():
    world = make_world()
    world.partners["partner-1"] = PartnerEntity("partner-1", "P", morale=80, trust=80)
    world.pets["pet-1"] = PetEntity("pet-1", "Pet", current_hp=100)
    actions = CoordinatedActionManager().propose(world)
    assert any(a.actor_id == "partner-1" and a.target_id == "enemy-1" for a in actions)
    assert any(a.actor_id == "pet-1" and a.target_id == "enemy-1" for a in actions)


def test_coordinated_manager_keeps_party_logic_observation_only():
    world = make_world(target_id=None)
    world.pets["pet-1"] = PetEntity("pet-1", "Pet", current_hp=10)
    actions = CoordinatedActionManager().propose(world)
    assert any(a.actor_id == "pet-1" for a in actions)
    assert world.state.target_id is None
