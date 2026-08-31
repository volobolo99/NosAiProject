"""The party coordinator, after the two implementations were merged into one.

`nosai/core/party_manager.py` declared a second `CoordinatedActionManager` that
nothing imported and no test covered. Its capabilities now live in the real
module; these tests exist so they are actually exercised rather than merely
carried over.
"""
import pytest

from nosai.core.contracts import ActionType, WorldState
from nosai.core.coordinated_action_manager import (
    PARTNER_ACTION_BY_BEHAVIOR,
    PET_ACTION_BY_BEHAVIOR,
    CoordinatedActionManager,
    PartyDecision,
)
from nosai.party import PartnerBehavior, PetBehavior, PetEntity, PartnerEntity


@pytest.fixture
def world():
    return WorldState(hp=100.0, mp=50.0, max_hp=100.0, target_id="mob-1")


def _pet(pet_id="pet-1", **kw):
    return PetEntity(pet_id=pet_id, name=pet_id, **kw)


def _partner(partner_id="partner-1", **kw):
    return PartnerEntity(partner_id=partner_id, name=partner_id, **kw)


# ---------------------------------------------------------------- the merge

def test_the_no_argument_constructor_still_works():
    # The Orchestrator builds it with no actors and feeds the world instead;
    # adding the constructor from the merged module must not break that.
    assert CoordinatedActionManager().decide.__self__ is not None


def test_every_pet_behaviour_maps_to_an_action():
    # The exhaustive table is the capability the merged module brought: an
    # unmapped behaviour must be impossible, not silently skipped.
    assert set(PET_ACTION_BY_BEHAVIOR) == set(PetBehavior)


def test_every_partner_behaviour_maps_to_an_action():
    assert set(PARTNER_ACTION_BY_BEHAVIOR) == set(PartnerBehavior)


def test_decide_returns_a_reason_and_a_confidence(world):
    manager = CoordinatedActionManager(pets={"pet-1": _pet()})

    decisions = manager.decide(world)

    assert len(decisions) == 1
    assert isinstance(decisions[0], PartyDecision)
    assert decisions[0].reason
    assert 0.0 <= decisions[0].confidence <= 1.0


def test_decide_covers_every_actor_it_holds(world):
    # propose() can drop an actor whose behaviour has no branch; decide() cannot.
    manager = CoordinatedActionManager(
        pets={"pet-1": _pet(), "pet-2": _pet(pet_id="pet-2")},
        partners={"partner-1": _partner()},
    )

    decisions = manager.decide(world)

    assert {d.actor_id for d in decisions} == {"pet-1", "pet-2", "partner-1"}


def test_tick_is_the_former_name_of_decide():
    assert CoordinatedActionManager.tick is CoordinatedActionManager.decide


def test_partner_confidence_is_its_obey_probability(world):
    partner = _partner()
    manager = CoordinatedActionManager(partners={"partner-1": partner})

    decision = manager.decide(world)[0]

    assert decision.confidence == pytest.approx(partner.obey_probability())


def test_no_actors_means_no_decisions(world):
    # Empty is a real answer here, distinct from "we could not tell".
    assert CoordinatedActionManager().decide(world) == []
