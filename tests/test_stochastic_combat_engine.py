import pytest

from nosai.core.contracts import ActionType, Goal, WorldState
from nosai.core.data_classification import DataSource
from nosai.core.safety import SafetyGate
from nosai.tactical.action_model import ActionBook, ActionSpec, EffectSpec
from nosai.tactical.combat_engine import CombatObservation, StochasticCombatEngine
from nosai.tactical.scheduling import PredictedTransition
from nosai.tactical.threat import SurvivalAction, ThreatCandidate


def _book() -> ActionBook:
    return ActionBook(
        [
            ActionSpec("strike", ActionType.ATTACK, cooldown_s=0.0, damage_ratio=0.06),
            ActionSpec(
                "nuke",
                ActionType.SKILL,
                cooldown_s=6.0,
                cast_s=1.5,
                mp_ratio_cost=0.2,
                damage_ratio=0.30,
            ),
            ActionSpec(
                "stun",
                ActionType.SKILL,
                cooldown_s=10.0,
                mp_ratio_cost=0.1,
                damage_ratio=0.02,
                effect=EffectSpec("stun", 0.4, 2.0, incoming_damage_scale=0.0),
            ),
            ActionSpec("potion", ActionType.RECOVER, cooldown_s=5.0, heal_ratio=0.4),
        ]
    )


def _engine(book: ActionBook | None = None, ready: bool = True) -> StochasticCombatEngine:
    book = book or _book()
    engine = StochasticCombatEngine(book, recovery_action_id="potion")
    if ready:
        for action_id in book.ids:
            engine.reconcile_cooldown(action_id, observed_ready=True, at=0.0)
    return engine


def _world(**overrides) -> WorldState:
    base = dict(
        hp=90.0, max_hp=100.0, mp=80.0, max_mp=100.0,
        target_id="mob-1", target_hp=100.0, tick_id=1,
    )
    base.update(overrides)
    return WorldState(**base)


def test_the_proposal_is_accepted_by_the_safety_gate():
    """The engine proposes into an existing boundary; it does not replace one.

    An offensive proposal aimed anywhere but ``world.target_id`` is rejected by
    the gate, so the engine must never emit one.
    """
    engine = _engine()
    world = _world()
    frame = engine.evaluate(CombatObservation(now=1.0, world=world))

    result = SafetyGate().evaluate(frame.decision.action, world)
    assert result.allowed is True
    if frame.decision.action.action in {ActionType.ATTACK, ActionType.SKILL}:
        assert frame.decision.action.target_id == world.target_id


def test_survival_preempts_a_winning_attack():
    """Tier C answers before Tier B is consulted.

    A burst that will end the character cannot be out-voted by a high-value
    attack, however good that attack looks to the search.
    """
    engine = _engine()
    for now, hp in ((0.0, 100.0), (0.5, 65.0), (1.0, 30.0)):
        frame = engine.evaluate(CombatObservation(now=now, world=_world(hp=hp, tick_id=int(now * 2))))

    assert frame.survival.action is SurvivalAction.DISENGAGE
    assert frame.decision.action.action is ActionType.MOVE
    assert frame.decision.action.parameters["intent"] == "disengage"
    assert frame.search is None


def test_a_moderate_drain_proposes_the_recovery_action():
    engine = _engine()
    for now, hp in ((0.0, 100.0), (0.5, 75.0), (1.0, 50.0)):
        frame = engine.evaluate(CombatObservation(now=now, world=_world(hp=hp, tick_id=int(now * 2))))

    assert frame.survival.action is SurvivalAction.RECOVER
    assert frame.decision.action.action is ActionType.RECOVER
    assert frame.decision.action.parameters["action_id"] == "potion"


def test_unknown_readiness_withholds_offence_but_not_recovery():
    """The asymmetry is deliberate and matches ``ActionPriority.UNKNOWN_SURVIVAL``.

    A wasted offensive frame costs uptime and desynchronises the shadow clock; a
    withheld recovery costs the character.
    """
    engine = _engine(ready=False)
    frame = engine.evaluate(CombatObservation(now=1.0, world=_world()))

    withheld = dict(frame.withheld)
    assert "readiness unknown" in withheld["strike"]
    assert "readiness unknown" in withheld["nuke"]
    assert "potion" not in withheld
    assert frame.decision.action.action is ActionType.RECOVER


def test_a_cooling_action_is_withheld_from_the_search():
    engine = _engine()
    engine.note_execution("nuke", at=0.5)
    frame = engine.evaluate(CombatObservation(now=1.0, world=_world()))

    assert "cooling" in dict(frame.withheld)["nuke"]
    assert "nuke" not in {item.action_id for item in frame.search.per_action}


def test_a_cast_that_will_be_interrupted_is_withheld():
    """Tier A gates Tier B rather than letting it plan around a doomed cast."""
    engine = _engine()
    frame = engine.evaluate(
        CombatObservation(
            now=1.0,
            world=_world(),
            predicted_transitions=(PredictedTransition(1.2, 0.9, "cleave"),),
        )
    )

    assert "interrupt-adjusted yield" in dict(frame.withheld)["nuke"]
    assert frame.decision.action.parameters.get("action_id") != "nuke"


def test_retargeting_is_a_recommendation_and_never_an_action():
    """``ActionType`` has no member for acquiring a target.

    Emitting an offensive action against a different entity would be rejected by
    the SafetyGate on every frame, so the tier reports its conclusion and leaves
    the contract change to an ADR.
    """
    engine = _engine()
    frame = engine.evaluate(
        CombatObservation(
            now=1.0,
            world=_world(),
            threats=(
                ThreatCandidate("mob-1", distance=30.0, hp_ratio=1.0),
                ThreatCandidate("mob-2", distance=1.0, hp_ratio=0.05),
            ),
        )
    )

    assert frame.retarget_to == "mob-2"
    assert frame.decision.action.target_id in (None, "mob-1")


def test_no_retarget_is_reported_when_the_observed_target_already_leads():
    engine = _engine()
    frame = engine.evaluate(
        CombatObservation(
            now=1.0,
            world=_world(),
            threats=(
                ThreatCandidate("mob-1", distance=1.0, hp_ratio=0.05),
                ThreatCandidate("mob-2", distance=30.0, hp_ratio=1.0),
            ),
        )
    )
    assert frame.retarget_to is None


def test_no_observed_target_yields_noop():
    engine = _engine()
    frame = engine.evaluate(CombatObservation(now=1.0, world=_world(target_id=None)))

    assert frame.decision.action.action is ActionType.NOOP
    assert "no observed target" in frame.decision.reasoning


def test_an_unreadable_mp_bar_withholds_costed_actions():
    """Affordability is unknowable without a scale, so only free actions remain."""
    engine = _engine()
    frame = engine.evaluate(CombatObservation(now=1.0, world=_world(max_mp=None)))

    withheld = dict(frame.withheld)
    assert "MP ratio unobservable" in withheld["nuke"]
    assert "MP ratio unobservable" in withheld["stun"]
    assert frame.decision.action.parameters.get("action_id") in {"strike", "potion"}


def test_an_unreadable_target_hp_withholds_the_kill_bonus():
    """Unknown is not full health, and it is not an almost-dead target either."""
    engine = _engine()
    frame = engine.evaluate(CombatObservation(now=1.0, world=_world(target_hp=None)))
    assert "target HP unobserved" in frame.decision.reasoning


def test_decide_refuses_to_answer_for_a_tick_it_never_evaluated():
    """A stale proposal is indistinguishable from a current one at the gate."""
    engine = _engine()
    assert engine.decide(_world(tick_id=1), Goal("grind")).action.action is ActionType.NOOP

    engine.evaluate(CombatObservation(now=1.0, world=_world(tick_id=1)))
    assert engine.decide(_world(tick_id=1), Goal("grind")).action.action is not ActionType.NOOP
    assert engine.decide(_world(tick_id=2), Goal("grind")).action.action is ActionType.NOOP


def test_re_evaluating_one_instant_does_not_bias_the_trend():
    engine = _engine()
    engine.evaluate(CombatObservation(now=1.0, world=_world()))
    engine.evaluate(CombatObservation(now=1.0, world=_world()))
    frame = engine.evaluate(CombatObservation(now=2.0, world=_world(hp=80.0, tick_id=2)))

    assert frame.survival.velocity.samples == 2


def test_the_frame_is_reproducible_for_a_tick():
    engine_a, engine_b = _engine(), _engine()
    world = _world(tick_id=42)
    first = engine_a.evaluate(CombatObservation(now=1.0, world=world, target_class="ELITE"))
    second = engine_b.evaluate(CombatObservation(now=1.0, world=world, target_class="ELITE"))

    assert first.decision.action.parameters == second.decision.action.parameters
    assert first.search.per_action == second.search.per_action


def test_learning_changes_what_the_engine_reaches_for():
    """The loop closes: measured resistance has to move the decision.

    A stun the encounter resists must stop being chosen, otherwise the matrix is
    bookkeeping rather than policy.

    Incoming damage is supplied because that is the only thing this stun is
    worth anything against: it suppresses the target's output, so with nothing
    incoming its landing rate is correctly irrelevant to the value function.
    """
    world = _world(tick_id=11)
    observation = CombatObservation(
        now=1.0, world=world, target_class="BOSS", incoming_dps=0.4
    )

    optimistic = _engine()
    optimistic.note_effect_outcome("stun", "BOSS", applied=True, weight=40.0)
    before = optimistic.evaluate(observation)

    resistant = _engine()
    for _ in range(60):
        resistant.note_effect_outcome("stun", "BOSS", applied=False)
    after = resistant.evaluate(observation)

    stun_before = next(v for v in before.search.per_action if v.action_id == "stun")
    stun_after = next(v for v in after.search.per_action if v.action_id == "stun")
    assert stun_after.mean_value < stun_before.mean_value
    assert resistant.matrix.posterior("stun", "BOSS").mean < 0.15


def test_confidence_is_a_bounded_share_of_the_search_budget():
    engine = _engine()
    frame = engine.evaluate(CombatObservation(now=1.0, world=_world()))
    assert 0.0 <= frame.decision.confidence <= 1.0


def test_the_risk_term_is_reported_as_unknown_before_a_trend_exists():
    """Zero incoming damage is not an observation that the fight is safe."""
    engine = _engine()
    frame = engine.evaluate(CombatObservation(now=1.0, world=_world()))
    assert frame.risk_source is DataSource.UNKNOWN

    live = engine.evaluate(
        CombatObservation(now=2.0, world=_world(tick_id=2), incoming_dps=0.3)
    )
    assert live.risk_source is DataSource.LIVE


def test_an_unknown_recovery_action_is_rejected_at_construction():
    with pytest.raises(ValueError):
        StochasticCombatEngine(_book(), recovery_action_id="elixir")
