import random

import pytest

from nosai.core.contracts import ActionType
from nosai.tactical.action_model import ActionBook, ActionSpec, EffectSpec
from nosai.tactical.search import (
    WAIT_ACTION_ID,
    CombatSimState,
    CombatSimulator,
    MonteCarloCombatSearch,
    SearchConfig,
)
from nosai.tactical.stochastic import StochasticTransitionMatrix


def _book() -> ActionBook:
    return ActionBook(
        [
            ActionSpec("weak", ActionType.ATTACK, cooldown_s=0.0, damage_ratio=0.02),
            ActionSpec("strong", ActionType.SKILL, cooldown_s=0.0, damage_ratio=0.25),
        ]
    )


def _stun_book() -> ActionBook:
    return ActionBook(
        [
            ActionSpec("slow_cast", ActionType.SKILL, cooldown_s=0.0, cast_s=3.0, damage_ratio=0.0),
            ActionSpec(
                "stun",
                ActionType.SKILL,
                cooldown_s=10.0,
                damage_ratio=0.0,
                effect=EffectSpec("stun", 1.0, 2.0, incoming_damage_scale=0.0),
            ),
        ]
    )


def _root(**overrides) -> CombatSimState:
    base = dict(t=0.0, own_hp=1.0, own_mp=1.0, target_hp=1.0, incoming_dps=0.0)
    base.update(overrides)
    return CombatSimState(**base)


def _search(book: ActionBook, **config) -> MonteCarloCombatSearch:
    return MonteCarloCombatSearch(
        book, StochasticTransitionMatrix(book), SearchConfig(**config)
    )


def test_search_is_reproducible_for_a_fixed_seed():
    """Monte Carlo does not have to mean non-deterministic.

    Every draw comes from one seeded generator, so the search stays a pure
    function of its inputs and a replayed frame reaches the same decision.
    """
    search = _search(_book(), iterations=128)
    first = search.search(_root(), seed=1234)
    second = search.search(_root(), seed=1234)
    different = search.search(_root(), seed=99)

    assert first.action_id == second.action_id
    assert first.expected_value == second.expected_value
    assert first.per_action == second.per_action
    assert different.seed == 99


def test_search_prefers_the_higher_damage_action():
    result = _search(_book(), iterations=400).search(_root(), seed=7)
    assert result.action_id == "strong"
    assert result.expected_value > 0.0
    assert result.depth == 4


def test_restriction_withholds_an_action_from_the_root():
    """Tier A reaches the search here.

    Actions whose readiness is unknown are withheld before any budget is spent
    on them, rather than scored and then discarded.
    """
    result = _search(_book(), iterations=200).search(_root(), seed=7, restrict_to=("weak",))
    assert result.action_id == "weak"
    assert {item.action_id for item in result.per_action} == {"weak"}


def test_the_search_avoids_a_line_that_kills_the_character():
    """Risk is weighted above damage, so a lethal rotation must lose.

    ``slow_cast`` stands in the incoming fire for three seconds; ``stun``
    suppresses it. At 40% HP against 0.2 HP/s the slow cast is fatal inside the
    horizon and must not be chosen.

    Regression for backing V(s') up an edge instead of Q(s,a): dying ends the
    episode, so the state after the fatal cast has value zero and the lethal
    line outscored every survivable one. The immediate reward has to travel with
    the edge.
    """
    book = _stun_book()
    search = MonteCarloCombatSearch(
        book, StochasticTransitionMatrix(book), SearchConfig(iterations=400)
    )
    result = search.search(_root(own_hp=0.4, incoming_dps=0.2), seed=3)
    assert result.action_id == "stun"


def test_incoming_damage_is_integrated_only_over_the_seconds_the_stun_covers():
    """Regression: a 2 s stun must not absorb a 3 s action.

    Holding the effect scale constant across the whole step is the easy version
    of this and it over-values control effects by exactly the uncovered tail,
    which is what makes a search open every fight with a stun it does not need.
    """
    book = _stun_book()
    simulator = CombatSimulator(book, StochasticTransitionMatrix(book))
    rng = random.Random(0)

    stunned, _, elapsed = simulator.step(
        _root(incoming_dps=0.2, effects=(("stun", 2.0),)), "slow_cast", None, rng
    )
    exposed, _, _ = simulator.step(_root(incoming_dps=0.2), "slow_cast", None, rng)

    assert elapsed == 3.0
    # Only the final uncovered second lands: 0.2 * (3.0 - 2.0) = 0.2.
    assert stunned.own_hp == pytest.approx(0.8)
    assert exposed.own_hp == pytest.approx(0.4)


def test_waiting_is_the_fallback_when_nothing_is_off_cooldown():
    book = _book()
    simulator = CombatSimulator(book, StochasticTransitionMatrix(book))
    blocked = _root(cooldowns=(("strong", 4.0), ("weak", 2.0)))

    assert simulator.legal_actions(blocked) == (WAIT_ACTION_ID,)

    # Waiting advances to the nearest expiry rather than to a fixed step.
    after, _, elapsed = simulator.step(blocked, WAIT_ACTION_ID, None, random.Random(0))
    assert elapsed == 2.0
    assert after.cooldown_of("weak") == 0.0
    assert after.cooldown_of("strong") == pytest.approx(2.0)


def test_a_root_with_nothing_committable_proposes_no_action():
    result = _search(_book(), iterations=64).search(
        _root(cooldowns=(("strong", 4.0), ("weak", 4.0))), seed=1, restrict_to=()
    )
    assert result.action_id is None
    assert "wait" in result.reason.lower()


def test_an_unaffordable_action_is_not_legal():
    book = ActionBook(
        [
            ActionSpec("free", ActionType.ATTACK, cooldown_s=0.0, damage_ratio=0.02),
            ActionSpec("costly", ActionType.SKILL, cooldown_s=0.0, mp_ratio_cost=0.5,
                       damage_ratio=0.4),
        ]
    )
    simulator = CombatSimulator(book, StochasticTransitionMatrix(book))
    assert simulator.legal_actions(_root(own_mp=0.1)) == ("free",)
    assert simulator.legal_actions(_root(own_mp=0.9)) == ("costly", "free")


def test_the_simulator_does_not_mutate_its_input_state():
    book = _book()
    simulator = CombatSimulator(book, StochasticTransitionMatrix(book))
    root = _root(incoming_dps=0.3)
    simulator.step(root, "strong", None, random.Random(0))
    assert root == _root(incoming_dps=0.3)
