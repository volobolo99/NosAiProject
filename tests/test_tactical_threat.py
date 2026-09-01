import pytest

from nosai.core.data_classification import DataSource
from nosai.tactical.threat import (
    BurstMonitor,
    SurvivalAction,
    ThreatCandidate,
    ThreatEvaluator,
    ThreatWeights,
)

RECOVER_BUDGET_S = 1.2
ESCAPE_BUDGET_S = 0.5


def _falling(monitor: BurstMonitor, samples: tuple[tuple[float, float], ...]) -> None:
    for at, hp in samples:
        monitor.observe(at, hp)


def test_priority_renormalises_over_the_observed_terms():
    """A term nothing could read is dropped, not scored zero.

    Zero would be a positive claim - that the entity is distant, at full health
    and immune - built out of a perception failure.
    """
    evaluator = ThreatEvaluator(ThreatWeights(0.5, 0.3, 0.2), proximity_half_life=8.0)
    score = evaluator.score(ThreatCandidate("mob", distance=8.0, hp_ratio=0.5))

    # Proximity 0.5 and health pressure 0.5, renormalised over the 0.8 of weight
    # that was observable.
    assert score.priority == pytest.approx(0.5)
    assert score.observed_weight_fraction == pytest.approx(0.8)
    assert score.source is DataSource.DERIVED
    assert "debuff_susceptibility" in score.reason


def test_an_entity_with_no_observable_term_is_ranked_last_but_kept():
    """Dropping it would present an incomplete field as a complete one."""
    evaluator = ThreatEvaluator()
    ranked = evaluator.prioritise(
        (ThreatCandidate("blind"), ThreatCandidate("seen", distance=2.0, hp_ratio=0.3))
    )

    assert [score.entity_id for score in ranked] == ["seen", "blind"]
    assert ranked[-1].priority is None
    assert ranked[-1].source is DataSource.UNKNOWN


def test_a_weakened_nearby_target_outranks_a_healthy_distant_one():
    """Kill priority runs on 1 - HP, not on HP.

    Weighting the health ratio upward as written would rank a full-health enemy
    above one at 5%, which is the opposite of finishing a fight.
    """
    ranked = ThreatEvaluator().prioritise(
        (
            ThreatCandidate("healthy_far", distance=40.0, hp_ratio=1.0, debuff_susceptibility=0.5),
            ThreatCandidate("hurt_near", distance=2.0, hp_ratio=0.05, debuff_susceptibility=0.5),
        )
    )
    assert ranked[0].entity_id == "hurt_near"


def test_velocity_needs_two_samples_at_distinct_timestamps():
    monitor = BurstMonitor()
    assert monitor.velocity().source is DataSource.UNKNOWN

    monitor.observe(1.0, 0.9)
    assert monitor.velocity().slope_per_s is None

    monitor.observe(1.0, 0.8)
    degenerate = monitor.velocity()
    assert degenerate.slope_per_s is None
    assert "one timestamp" in degenerate.reason


def test_least_squares_recovers_the_drain_rate():
    monitor = BurstMonitor()
    _falling(monitor, ((0.0, 1.0), (0.5, 0.65), (1.0, 0.3)))
    velocity = monitor.velocity()

    assert velocity.slope_per_s == pytest.approx(-0.7)
    assert velocity.samples == 3
    assert velocity.source is DataSource.DERIVED


def test_burst_disengages_while_hp_is_still_above_the_static_floor():
    """The trend fires before the threshold a static rule would wait for.

    At 30% HP a floor of 25% has not been crossed, yet 0.7 bar/s leaves under
    half a second of life - less than a potion takes to land.
    """
    monitor = BurstMonitor(absolute_floor=0.25)
    _falling(monitor, ((0.0, 1.0), (0.5, 0.65), (1.0, 0.3)))

    verdict = monitor.verdict(0.3, RECOVER_BUDGET_S, ESCAPE_BUDGET_S)

    assert verdict.action is SurvivalAction.DISENGAGE
    assert verdict.time_to_death_s == pytest.approx(0.3 / 0.7)
    assert verdict.source is DataSource.DERIVED


def test_a_moderate_drain_recovers_rather_than_disengaging():
    monitor = BurstMonitor(absolute_floor=0.25)
    _falling(monitor, ((0.0, 1.0), (0.5, 0.75), (1.0, 0.5)))

    verdict = monitor.verdict(0.5, RECOVER_BUDGET_S, ESCAPE_BUDGET_S)

    assert verdict.action is SurvivalAction.RECOVER
    assert verdict.time_to_death_s == pytest.approx(1.0)
    # The threshold is a function of the drain rate, not a constant.
    assert verdict.dynamic_threshold == pytest.approx(0.5 * RECOVER_BUDGET_S)


def test_a_healthy_character_under_the_same_drain_keeps_fighting():
    monitor = BurstMonitor(absolute_floor=0.25)
    _falling(monitor, ((0.0, 1.0), (0.5, 0.75), (1.0, 0.5)))
    assert monitor.verdict(0.95, RECOVER_BUDGET_S, ESCAPE_BUDGET_S).action is SurvivalAction.CONTINUE


def test_a_slow_bleed_still_hits_the_absolute_floor():
    """The floor is the backstop the trend detector cannot provide.

    A drain this gentle never projects an alarming time to death, but it still
    ends the character.
    """
    monitor = BurstMonitor(absolute_floor=0.25)
    _falling(monitor, ((0.0, 0.30), (0.5, 0.27), (1.0, 0.24)))

    verdict = monitor.verdict(0.24, RECOVER_BUDGET_S, ESCAPE_BUDGET_S)

    assert verdict.action is SurvivalAction.RECOVER
    assert verdict.time_to_death_s > RECOVER_BUDGET_S
    assert verdict.dynamic_threshold < 0.25


def test_unknown_hp_ratio_fails_closed_to_recovery():
    """Regression: absolute HP carries no scale, so no threshold can read it.

    Same reasoning as ``ActionPriority.UNKNOWN_SURVIVAL`` in
    ``nosai.core.tactical_ranking``. Attempting an unneeded recovery costs one
    frame; skipping a needed one costs the character.
    """
    verdict = BurstMonitor().verdict(None, RECOVER_BUDGET_S, ESCAPE_BUDGET_S)

    assert verdict.action is SurvivalAction.RECOVER
    assert verdict.source is DataSource.UNKNOWN
    assert verdict.time_to_death_s is None
    assert verdict.dynamic_threshold is None


def test_unreadable_frames_are_dropped_rather_than_carried_forward():
    """Holding the last value would flatten the slope exactly when it matters.

    Vision struggles hardest during the burst it most needs to detect.
    """
    monitor = BurstMonitor()
    _falling(monitor, ((0.0, 1.0), (0.5, 0.65)))
    monitor.observe(0.75, None)
    monitor.observe(1.0, 0.3)

    velocity = monitor.velocity()
    assert velocity.samples == 3
    assert velocity.slope_per_s == pytest.approx(-0.7)


def test_samples_older_than_the_window_stop_counting():
    monitor = BurstMonitor(window_s=1.0)
    _falling(monitor, ((0.0, 1.0), (5.0, 0.9), (5.5, 0.85)))
    assert monitor.velocity().samples == 2


def test_escape_budget_may_not_exceed_the_recovery_budget():
    with pytest.raises(ValueError):
        BurstMonitor().verdict(0.5, recover_budget_s=0.5, escape_budget_s=1.0)
