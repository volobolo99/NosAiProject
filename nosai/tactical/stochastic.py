r"""Tier B (learning) - online Bayesian estimation of status-effect landing rates.

A client's stated "40% chance to stun" describes the action, not the encounter.
Against a resistant target the realised rate is lower, and the only way to find
out is to fire and watch. This module keeps one Beta-Bernoulli posterior per
``(action, target class)`` pair, seeded from the stated probability and moved by
outcomes.

Conjugacy makes the update exact and closed-form. With prior
:math:`p \sim \mathrm{Beta}(\alpha_0, \beta_0)` and Bernoulli evidence,

.. math::
   P(p \mid s, f) \;\propto\; p^{s}(1-p)^{f} \cdot p^{\alpha_0-1}(1-p)^{\beta_0-1}
   \;=\; \mathrm{Beta}(\alpha_0 + s,\; \beta_0 + f)

so the posterior mean after :math:`s` applications and :math:`f` resists is
:math:`\hat p = (\alpha_0 + s)/(\alpha_0 + \beta_0 + s + f)`.

The prior is written as :math:`\alpha_0 = \kappa p_0`, :math:`\beta_0 = \kappa(1-p_0)`,
which puts the stated probability :math:`p_0` at the prior mean and expresses
:math:`\kappa` as the pseudo-count of evidence it is worth. A never-fired pair
therefore reports exactly the stated number - and reports ``observations == 0``
alongside it, so a caller can always tell a claim from a measurement.
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass

from nosai.core.data_classification import DataSource

from .action_model import ActionBook

# Weight of the client's own claim, in observations. Eight is deliberately low:
# tooltips describe an unresisted target, so a dozen real casts should be able to
# overrule one. It is not zero because a cold start with no prior would let a
# single unlucky resist read as "this never works".
DEFAULT_PRIOR_STRENGTH = 8.0

# Exponential forgetting factor. Resistances are not stationary - buffs, debuffs
# and zone changes move them - so evidence decays toward the prior at each
# update, bounding the effective sample size at 1/(1-lambda) = 50 observations.
DEFAULT_FORGETTING = 0.98

# Target class used when perception cannot classify the target. It is a real,
# separate bucket rather than a merge into a global one: lumping unclassified
# targets together with known ones would let a boss's resistances pollute the
# estimate used against trash.
UNCLASSIFIED_TARGET = "UNCLASSIFIED"


@dataclass(frozen=True)
class BetaPosterior:
    r"""Posterior over one conditional probability :math:`P(\text{effect} \mid a, c)`."""

    action_id: str
    target_class: str
    effect_id: str
    alpha: float
    beta: float
    prior_alpha: float
    prior_beta: float
    successes: float
    failures: float

    @property
    def mean(self) -> float:
        r""":math:`\mathbb{E}[p] = \alpha / (\alpha + \beta)`."""
        return self.alpha / (self.alpha + self.beta)

    @property
    def variance(self) -> float:
        r""":math:`\mathrm{Var}[p] = \alpha\beta / \big((\alpha+\beta)^2(\alpha+\beta+1)\big)`."""
        total = self.alpha + self.beta
        return (self.alpha * self.beta) / (total * total * (total + 1.0))

    @property
    def observations(self) -> float:
        """Discounted evidence behind this posterior. Zero means "claim only"."""
        return self.successes + self.failures

    @property
    def nominal(self) -> float:
        """The stated probability this posterior started from."""
        return self.prior_alpha / (self.prior_alpha + self.prior_beta)

    @property
    def source(self) -> DataSource:
        """``DERIVED`` once evidence exists, ``UNKNOWN`` while only the claim does.

        The mean is well defined either way, but a caller weighing whether to
        spend a long cast on a control effect needs to know which of the two it
        is looking at.
        """
        return DataSource.DERIVED if self.observations > 0.0 else DataSource.UNKNOWN

    def credible_interval(self, z: float = 2.0) -> tuple[float, float]:
        r"""Normal approximation :math:`\hat p \pm z\sigma`, clamped to :math:`[0,1]`.

        Reporting only. The approximation is poor for :math:`\hat p` near the
        bounds with little evidence, which is precisely where decisions must not
        rest on it - the search samples the exact Beta instead.
        """
        sigma = math.sqrt(self.variance)
        return (max(0.0, self.mean - z * sigma), min(1.0, self.mean + z * sigma))

    def sample(self, rng: random.Random) -> float:
        r"""Thompson draw :math:`p \sim \mathrm{Beta}(\alpha, \beta)`.

        Sampling rather than substituting the mean is what makes the rollouts in
        :mod:`nosai.tactical.search` explore correctly: an action whose rate is
        merely *unmeasured* still gets tried, while one measured as unreliable
        stops being drawn favourably.
        """
        return rng.betavariate(self.alpha, self.beta)


@dataclass
class _Cell:
    alpha: float
    beta: float
    successes: float = 0.0
    failures: float = 0.0


class StochasticTransitionMatrix:
    r"""Online :math:`M[a, c] = P(\text{effect} \mid \text{action } a, \text{target class } c)`.

    Each update first decays the cell toward its prior,

    .. math:: \alpha \leftarrow \alpha_0 + \lambda(\alpha - \alpha_0),
              \qquad \beta \leftarrow \beta_0 + \lambda(\beta - \beta_0)

    and then folds in the observation. The geometric series bounds the steady
    state at :math:`\mathrm{ESS} = w/(1-\lambda)`, so the estimate tracks a
    changing target instead of averaging over a resistance the encounter no
    longer has.
    """

    def __init__(
        self,
        book: ActionBook,
        prior_strength: float = DEFAULT_PRIOR_STRENGTH,
        forgetting: float = DEFAULT_FORGETTING,
    ) -> None:
        if prior_strength <= 0.0:
            raise ValueError("prior_strength must be positive")
        if not 0.0 < forgetting <= 1.0:
            raise ValueError("forgetting must lie in (0, 1]")
        self.book = book
        self.prior_strength = prior_strength
        self.forgetting = forgetting
        self._cells: dict[tuple[str, str], _Cell] = {}

    @property
    def effective_sample_size(self) -> float:
        r"""Steady-state evidence ceiling :math:`1/(1-\lambda)`, or ``inf`` when nothing is forgotten."""
        if self.forgetting >= 1.0:
            return math.inf
        return 1.0 / (1.0 - self.forgetting)

    def _prior(self, action_id: str) -> tuple[float, float]:
        spec = self.book[action_id]
        if spec.effect is None:
            raise ValueError(f"{action_id} declares no effect to learn about")
        p0 = spec.effect.nominal_probability
        # Clamped away from the open bounds: Beta(0, k) is degenerate, and a
        # stated 0% or 100% is a claim like any other, not a certainty.
        p0 = min(max(p0, 1e-6), 1.0 - 1e-6)
        return self.prior_strength * p0, self.prior_strength * (1.0 - p0)

    def _cell(self, action_id: str, target_class: str) -> _Cell:
        key = (action_id, target_class)
        cell = self._cells.get(key)
        if cell is None:
            prior_alpha, prior_beta = self._prior(action_id)
            cell = _Cell(prior_alpha, prior_beta)
            self._cells[key] = cell
        return cell

    def posterior(self, action_id: str, target_class: str | None) -> BetaPosterior:
        """Current belief for one pair. An unseen pair returns the prior, unmodified."""
        spec = self.book[action_id]
        if spec.effect is None:
            raise ValueError(f"{action_id} declares no effect to learn about")
        klass = target_class or UNCLASSIFIED_TARGET
        prior_alpha, prior_beta = self._prior(action_id)
        cell = self._cells.get((action_id, klass))
        if cell is None:
            cell = _Cell(prior_alpha, prior_beta)
        return BetaPosterior(
            action_id=action_id,
            target_class=klass,
            effect_id=spec.effect.effect_id,
            alpha=cell.alpha,
            beta=cell.beta,
            prior_alpha=prior_alpha,
            prior_beta=prior_beta,
            successes=cell.successes,
            failures=cell.failures,
        )

    def observe(
        self,
        action_id: str,
        target_class: str | None,
        applied: bool,
        weight: float = 1.0,
    ) -> BetaPosterior:
        """Fold one execution outcome into the matrix and return the new posterior.

        ``applied`` is whether the effect was *observed on the target*, which is
        not the same as the action succeeding. A caller that could not observe
        the target's debuff bar must not call this with ``False``: an unobserved
        effect is unknown, and recording it as a resist would teach the matrix
        that the action does not work.
        """
        if weight <= 0.0:
            raise ValueError("weight must be positive")
        klass = target_class or UNCLASSIFIED_TARGET
        prior_alpha, prior_beta = self._prior(action_id)
        cell = self._cell(action_id, klass)

        lam = self.forgetting
        cell.alpha = prior_alpha + lam * (cell.alpha - prior_alpha)
        cell.beta = prior_beta + lam * (cell.beta - prior_beta)
        cell.successes *= lam
        cell.failures *= lam

        if applied:
            cell.alpha += weight
            cell.successes += weight
        else:
            cell.beta += weight
            cell.failures += weight

        return self.posterior(action_id, klass)

    def known_pairs(self) -> tuple[tuple[str, str], ...]:
        return tuple(sorted(self._cells))

    def susceptibility(self, target_class: str | None) -> float | None:
        """Best posterior mean across every control effect known for this class.

        Returns ``None`` when no action in the book carries an effect, so the
        threat weighting can drop the term rather than score the target zero -
        an unmeasurable susceptibility is not an immunity.
        """
        means = [
            self.posterior(spec.action_id, target_class).mean
            for spec in self.book
            if spec.effect is not None
        ]
        return max(means) if means else None
