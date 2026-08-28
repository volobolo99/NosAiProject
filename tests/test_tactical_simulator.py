from app.simulation.tactical import (
    BeamSearchPlanner,
    Combatant,
    Element,
    TacticalAction,
    TacticalSimulator,
    TacticalState,
)


def make_state() -> TacticalState:
    player = Combatant(
        id="player", hp=100, max_hp=100, mp=80, max_mp=80,
        attack=30, defense=10, element=Element.FIRE,
    )
    enemy = Combatant(
        id="e1", hp=40, max_hp=40, mp=0, max_mp=0,
        attack=10, defense=5, element=Element.SHADOW,
        resistance={Element.FIRE: 0},
    )
    return TacticalState(tick=0, time_left=30.0, player=player, enemies=(enemy,), potions=2)


def test_element_advantage_is_reflected_in_legal_attack():
    sim = TacticalSimulator()
    attacks = [a for a in sim.legal_actions(make_state()) if a.name == "attack"]
    assert attacks
    assert attacks[0].expected_damage > 20


def test_step_is_reproducible_with_seed():
    sim = TacticalSimulator()
    action = [a for a in sim.legal_actions(make_state()) if a.name == "attack"][0]
    first = sim.rollout(make_state(), (action,), seed=42)
    second = sim.rollout(make_state(), (action,), seed=42)
    assert first == second


def test_heal_consumes_potion_only_when_available_and_low_hp():
    sim = TacticalSimulator()
    state = make_state()
    damaged = TacticalState(**{**state.__dict__, "player": state.player.__class__(**{**state.player.__dict__, "hp": 40})})
    heal = [a for a in sim.legal_actions(damaged) if a.name == "heal"]
    assert heal
    result = sim.rollout(damaged, (heal[0],), seed=1)
    assert result.potions == 1
    assert result.player.hp > damaged.player.hp


def test_beam_search_returns_bounded_plan():
    sim = TacticalSimulator()
    planner = BeamSearchPlanner(sim, width=4, depth=4, rollouts=4)
    result = planner.plan(make_state(), seed=7)
    assert len(result.actions) <= 4
    assert result.explored_nodes > 0
    assert 0.0 <= result.success_probability <= 1.0
