import pytest

from nosai.core.contracts import ActionType
from nosai.core.data_classification import DataSource
from nosai.tactical.action_model import ActionBook, ActionSpec, EffectSpec
from nosai.tactical.stochastic import UNCLASSIFIED_TARGET, StochasticTransitionMatrix


def _book() -> ActionBook:
    return ActionBook(
        [
            ActionSpec("strike", ActionType.ATTACK, cooldown_s=0.0, damage_ratio=0.05),
            ActionSpec(
                "stun",
                ActionType.SKILL,
                cooldown_s=10.0,
                damage_ratio=0.02,
                effect=EffectSpec("stun", nominal_probability=0.4, duration_s=2.0,
                                  incoming_damage_scale=0.0),
            ),
        ]
    )


def test_unobserved_pair_reports_the_claim_and_declares_it_unmeasured():
    """The tooltip number is a prior, and the caller must be able to tell.

    Returning 0.4 with no way to distinguish "the client says 40%" from "we
    measured 40% over 200 casts" is what makes a learning loop indistinguishable
    from a hard-coded constant.
    """
    posterior = StochasticTransitionMatrix(_book()).posterior("stun", "ELITE")

    assert posterior.mean == pytest.approx(0.4)
    assert posterior.nominal == pytest.approx(0.4)
    assert posterior.observations == 0.0
    assert posterior.source is DataSource.UNKNOWN


def test_resists_pull_the_posterior_below_the_claim():
    matrix = StochasticTransitionMatrix(_book(), prior_strength=8.0, forgetting=1.0)
    for _ in range(20):
        matrix.observe("stun", "ELITE", applied=False)

    posterior = matrix.posterior("stun", "ELITE")
    # alpha stays 3.2, beta becomes 4.8 + 20 -> mean = 3.2 / 28 ~= 0.114
    assert posterior.mean == pytest.approx(3.2 / 28.0)
    assert posterior.mean < posterior.nominal
    assert posterior.source is DataSource.DERIVED


def test_evidence_moves_the_posterior_toward_what_was_measured():
    matrix = StochasticTransitionMatrix(_book(), prior_strength=8.0, forgetting=1.0)
    for _ in range(40):
        matrix.observe("stun", "TRASH", applied=True)
    assert matrix.posterior("stun", "TRASH").mean > 0.8


def test_forgetting_bounds_the_effective_sample_size():
    """A resistance that changed must be re-learnable.

    Without decay the counts are unbounded, so a boss phase measured over
    hundreds of casts would keep dominating the estimate long after the phase
    ended.
    """
    matrix = StochasticTransitionMatrix(_book(), forgetting=0.98)
    for _ in range(500):
        matrix.observe("stun", "ELITE", applied=False)

    posterior = matrix.posterior("stun", "ELITE")
    assert matrix.effective_sample_size == pytest.approx(50.0)
    assert posterior.observations <= matrix.effective_sample_size + 1e-6

    # And the estimate turns around within a bounded number of contrary samples.
    for _ in range(100):
        matrix.observe("stun", "ELITE", applied=True)
    assert matrix.posterior("stun", "ELITE").mean > 0.8


def test_target_classes_do_not_share_evidence():
    """Resistance is a property of the encounter, not of the action.

    Pooling classes would let one immune boss teach the engine that a control
    effect never works on anything.
    """
    matrix = StochasticTransitionMatrix(_book(), forgetting=1.0)
    for _ in range(30):
        matrix.observe("stun", "BOSS", applied=False)

    assert matrix.posterior("stun", "BOSS").mean < 0.15
    assert matrix.posterior("stun", "TRASH").mean == pytest.approx(0.4)
    assert matrix.posterior("stun", None).target_class == UNCLASSIFIED_TARGET


def test_an_unclassified_target_is_its_own_bucket():
    matrix = StochasticTransitionMatrix(_book(), forgetting=1.0)
    for _ in range(30):
        matrix.observe("stun", None, applied=False)

    assert matrix.posterior("stun", None).mean < 0.15
    assert matrix.posterior("stun", "ELITE").mean == pytest.approx(0.4)


def test_variance_shrinks_as_evidence_accumulates():
    matrix = StochasticTransitionMatrix(_book(), forgetting=1.0)
    wide = matrix.posterior("stun", "ELITE")
    for _ in range(40):
        matrix.observe("stun", "ELITE", applied=True)
    narrow = matrix.posterior("stun", "ELITE")

    assert narrow.variance < wide.variance
    low, high = narrow.credible_interval()
    assert 0.0 <= low <= narrow.mean <= high <= 1.0


def test_susceptibility_is_none_when_nothing_in_the_book_carries_an_effect():
    """An unmeasurable susceptibility is not an immunity.

    The threat weighting drops the term instead of scoring the entity zero.
    """
    plain = ActionBook([ActionSpec("strike", ActionType.ATTACK, cooldown_s=0.0, damage_ratio=0.05)])
    assert StochasticTransitionMatrix(plain).susceptibility("ELITE") is None
    assert StochasticTransitionMatrix(_book()).susceptibility("ELITE") == pytest.approx(0.4)


def test_learning_about_an_effectless_action_is_rejected():
    with pytest.raises(ValueError):
        StochasticTransitionMatrix(_book()).observe("strike", "ELITE", applied=True)
