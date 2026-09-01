import pytest

from nosai.core.contracts import ActionType
from nosai.core.data_classification import DataSource
from nosai.tactical.action_model import ActionBook, ActionSpec
from nosai.tactical.scheduling import (
    CastPhase,
    CastWindow,
    InterruptModel,
    PredictedTransition,
    ReadinessState,
    ShadowCooldownClock,
)


def _book() -> ActionBook:
    return ActionBook(
        [
            ActionSpec("strike", ActionType.ATTACK, cooldown_s=0.0, damage_ratio=0.05),
            ActionSpec(
                "nuke",
                ActionType.SKILL,
                cooldown_s=6.0,
                cast_s=1.5,
                animation_lock_s=0.5,
                damage_ratio=0.30,
            ),
            ActionSpec(
                "safe_nuke",
                ActionType.SKILL,
                cooldown_s=6.0,
                cast_s=1.5,
                damage_ratio=0.30,
                consumes_cooldown_on_interrupt=False,
            ),
        ]
    )


def test_readiness_is_unknown_before_any_execution_or_observation():
    """A never-seen action is unknown, not ready and not cooling.

    Reporting READY here would let the engine fire into a cooldown it has no
    evidence about; reporting COOLING would permanently withhold an action that
    perception simply has not looked at yet.
    """
    readiness = ShadowCooldownClock(_book()).readiness("nuke", now=10.0)
    assert readiness.state is ReadinessState.UNKNOWN
    assert readiness.remaining_s is None
    assert readiness.source is DataSource.UNKNOWN


def test_shadow_clock_predicts_the_cooldown_after_an_execution():
    clock = ShadowCooldownClock(_book())
    clock.note_execution("nuke", at=100.0)

    cooling = clock.readiness("nuke", now=101.0)
    assert cooling.state is ReadinessState.COOLING
    assert cooling.remaining_s == 5.0
    assert cooling.source is DataSource.DERIVED

    # The prediction survives the perception frames it never received.
    assert clock.readiness("nuke", now=106.5).state is ReadinessState.READY


def test_prediction_degrades_to_cached_past_the_confirmation_horizon():
    """Staleness changes the provenance, never the estimate.

    A dropped perception frame is not evidence of anything, so the clock keeps
    predicting - but it stops claiming the prediction is fresh.
    """
    clock = ShadowCooldownClock(_book(), confirmation_horizon_s=2.0)
    clock.note_execution("nuke", at=0.0)

    fresh = clock.readiness("nuke", now=1.0)
    stale = clock.readiness("nuke", now=5.0)

    assert fresh.source is DataSource.DERIVED
    assert stale.source is DataSource.CACHED
    assert stale.state is ReadinessState.COOLING
    assert stale.remaining_s == 1.0
    assert "unconfirmed by perception" in stale.reason


def test_observed_ready_snaps_the_clock_and_reports_drift():
    clock = ShadowCooldownClock(_book())
    clock.note_execution("nuke", at=0.0)

    drift = clock.reconcile("nuke", observed_ready=True, at=4.0)

    assert drift == 2.0
    assert clock.readiness("nuke", now=4.0).state is ReadinessState.READY
    assert clock.diagnostics("nuke").mean_absolute_drift_s == 2.0


def test_observed_cooling_past_prediction_leaves_the_remainder_unknown():
    """Regression: a greyed-out icon says "not ready", never "ready in N seconds".

    Substituting zero would assert readiness the observation contradicts, and
    substituting the full cooldown would invent a duration nothing measured.
    """
    clock = ShadowCooldownClock(_book())
    clock.note_execution("nuke", at=0.0)

    clock.reconcile("nuke", observed_ready=False, at=7.0)
    readiness = clock.readiness("nuke", now=7.0)

    assert readiness.state is ReadinessState.COOLING
    assert readiness.remaining_s is None
    assert readiness.source is DataSource.DERIVED
    assert "unobservable end" in readiness.reason


def test_instant_action_has_no_interrupt_window():
    forecast = InterruptModel(_book()).forecast(
        "strike", start_at=0.0, transitions=(PredictedTransition(0.0, 1.0, "cleave"),)
    )
    assert forecast.interrupt_probability == 0.0
    assert forecast.expected_value_multiplier == 1.0


def test_independent_transitions_compose_multiplicatively():
    forecast = InterruptModel(_book()).forecast(
        "safe_nuke",
        start_at=0.0,
        transitions=(
            PredictedTransition(0.4, 0.5, "cleave"),
            PredictedTransition(0.9, 0.5, "slam"),
            PredictedTransition(9.0, 1.0, "outside-the-window"),
        ),
    )
    # P = 1 - (1-0.5)(1-0.5) = 0.75, and the transition past the cast is excluded.
    assert forecast.interrupt_probability == 0.75
    assert forecast.completion_probability == pytest.approx(0.25)
    assert forecast.contributing == ("cleave", "slam")


def test_wasting_the_cooldown_can_drive_the_expected_yield_negative():
    """An action that loses its cooldown to an interrupt is worse than not casting.

    ``safe_nuke`` keeps its cooldown and so is never worth less than zero;
    ``nuke`` does not, and past a coin-flip interrupt chance it is a net loss.
    """
    model = InterruptModel(_book())
    transitions = (PredictedTransition(0.5, 0.8, "cleave"),)

    risky = model.forecast("nuke", 0.0, transitions)
    safe = model.forecast("safe_nuke", 0.0, transitions)

    assert risky.expected_value_multiplier < 0.0
    assert safe.expected_value_multiplier == pytest.approx(0.2)


def test_animation_cancel_is_free_while_a_cast_cancel_must_beat_the_residual():
    """The two cancels are not the same decision.

    Past ``cast_ends_at`` the payload already resolved, so cancelling only
    returns time. Before it, cancelling destroys the payload and has to be
    justified against what finishing is still worth.
    """
    book = _book()
    model = InterruptModel(book)
    window = CastWindow.for_action(book["nuke"], starts_at=0.0)

    mid_cast = model.should_cancel_cast(window, now=0.5, alternative_value=0.01)
    better = model.should_cancel_cast(window, now=0.5, alternative_value=0.9)
    locked = model.should_cancel_animation(window, now=1.7, has_queued_action=True)
    idle = model.should_cancel_animation(window, now=1.7, has_queued_action=False)

    assert window.phase_at(0.5) is CastPhase.CAST
    assert window.phase_at(1.7) is CastPhase.ANIMATION_LOCK
    assert mid_cast.cancel is False
    assert better.cancel is True
    assert locked.cancel is True
    assert idle.cancel is False


def test_survival_forces_a_cast_cancel_regardless_of_value():
    book = _book()
    window = CastWindow.for_action(book["nuke"], starts_at=0.0)
    verdict = InterruptModel(book).should_cancel_cast(
        window, now=0.5, alternative_value=0.0, forced=True
    )
    assert verdict.cancel is True
    assert "survival" in verdict.reason
