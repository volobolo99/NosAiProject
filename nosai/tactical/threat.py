r"""Tier C - adaptive threat evaluation and the burst-damage failsafe.

Two decisions live here, and they answer in a fixed order. *Should we still be
fighting?* is settled before *what should we be fighting?*, because a priority
list computed while the character is dying is a list of things it will not live
to attack.

Both refuse to substitute a number for a missing observation. A target whose HP
cannot be read is not a full-health target; a character whose HP ratio cannot be
derived is not a healthy one. Where a term is unobservable the weighting drops
it and renormalises, and where nothing is observable the verdict is ``UNKNOWN``
and the caller is told so.
"""
from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass
from enum import Enum

from nosai.core.data_classification import DataSource

# Distance at which the proximity term is worth half its maximum. Expressed in
# whatever unit perception reports distances in; only the ratio d/d0 matters.
DEFAULT_PROXIMITY_HALF_LIFE = 8.0

# Backstop HP ratio. The primary trigger is velocity-based, but a slow bleed
# never produces an alarming slope while still ending the character, so a floor
# stays under the trend detector rather than replacing it.
DEFAULT_ABSOLUTE_FLOOR = 0.25


@dataclass(frozen=True)
class ThreatWeights:
    r"""Weights of the priority form
    :math:`\mathrm{Priority} = w_1 x_{prox} + w_2 x_{hp} + w_3 x_{debuff}`.
    """

    proximity: float = 0.5
    health_pressure: float = 0.3
    debuff_susceptibility: float = 0.2

    def __post_init__(self) -> None:
        if min(self.proximity, self.health_pressure, self.debuff_susceptibility) < 0.0:
            raise ValueError("threat weights must be non-negative")
        if self.proximity + self.health_pressure + self.debuff_susceptibility <= 0.0:
            raise ValueError("at least one threat weight must be positive")


@dataclass(frozen=True)
class ThreatCandidate:
    """One observed entity, with every unreadable field left as ``None``."""

    entity_id: object
    distance: float | None = None
    hp_ratio: float | None = None
    debuff_susceptibility: float | None = None
    is_current_target: bool = False


@dataclass(frozen=True)
class ThreatScore:
    entity_id: object
    priority: float | None
    source: DataSource
    terms: tuple[tuple[str, float | None], ...]
    observed_weight_fraction: float
    reason: str


class ThreatEvaluator:
    r"""Ranks candidates by the weighted priority invariant.

    The health term is :math:`x_{hp} = 1 - h`, not :math:`h`. Taken literally,
    weighting the health *ratio* upward would rank a full-health enemy above one
    at 5%, which inverts kill priority: finishing a nearly-dead target removes
    its damage output from the fight, while opening on a fresh one adds to the
    incoming total. The specified symbol is kept; its orientation is the one
    that makes the invariant mean what it is for.

    When a term cannot be observed it is dropped and the remaining weights are
    renormalised,

    .. math:: \mathrm{Priority} = \frac{\sum_{i \in K} w_i x_i}{\sum_{i \in K} w_i}

    over the observed set :math:`K`. Scoring a missing term as zero would be a
    silent claim that the entity is far away, at full health and immune.
    """

    def __init__(
        self,
        weights: ThreatWeights | None = None,
        proximity_half_life: float = DEFAULT_PROXIMITY_HALF_LIFE,
    ) -> None:
        if proximity_half_life <= 0.0:
            raise ValueError("proximity_half_life must be positive")
        self.weights = weights or ThreatWeights()
        self.proximity_half_life = proximity_half_life

    def _proximity_term(self, distance: float | None) -> float | None:
        r""":math:`x_{prox} = (1 + d/d_0)^{-1}`, monotone decreasing into :math:`(0, 1]`."""
        if distance is None:
            return None
        return 1.0 / (1.0 + max(0.0, distance) / self.proximity_half_life)

    def score(self, candidate: ThreatCandidate) -> ThreatScore:
        weights = self.weights
        terms: list[tuple[str, float, float | None]] = [
            ("proximity", weights.proximity, self._proximity_term(candidate.distance)),
            (
                "health_pressure",
                weights.health_pressure,
                None if candidate.hp_ratio is None else 1.0 - candidate.hp_ratio,
            ),
            (
                "debuff_susceptibility",
                weights.debuff_susceptibility,
                candidate.debuff_susceptibility,
            ),
        ]

        total_weight = sum(weight for _, weight, _ in terms)
        observed_weight = sum(weight for _, weight, value in terms if value is not None)
        reported = tuple((name, value) for name, _, value in terms)
        missing = tuple(name for name, _, value in terms if value is None)

        if observed_weight <= 0.0:
            return ThreatScore(
                entity_id=candidate.entity_id,
                priority=None,
                source=DataSource.UNKNOWN,
                terms=reported,
                observed_weight_fraction=0.0,
                reason="no threat term could be observed",
            )

        weighted = sum(weight * value for _, weight, value in terms if value is not None)
        priority = weighted / observed_weight
        fraction = observed_weight / total_weight if total_weight > 0.0 else 0.0

        return ThreatScore(
            entity_id=candidate.entity_id,
            priority=priority,
            source=DataSource.DERIVED,
            terms=reported,
            observed_weight_fraction=fraction,
            reason=(
                f"renormalised over {fraction:.0%} of the weight; unobserved: {', '.join(missing)}"
                if missing
                else "all terms observed"
            ),
        )

    def prioritise(self, candidates: tuple[ThreatCandidate, ...]) -> tuple[ThreatScore, ...]:
        """Highest priority first; unscorable entities last, never dropped.

        An entity nothing could be read about is still an entity. Removing it
        from the list would present an incomplete field as a complete one.
        """
        scored = [self.score(candidate) for candidate in candidates]
        return tuple(
            sorted(
                scored,
                key=lambda item: (
                    item.priority is None,
                    -(item.priority if item.priority is not None else 0.0),
                    str(item.entity_id),
                ),
            )
        )


@dataclass(frozen=True)
class VelocityEstimate:
    r"""Least-squares HP trend :math:`\dot h` in bar fractions per second."""

    slope_per_s: float | None
    samples: int
    span_s: float
    source: DataSource
    reason: str


class SurvivalAction(str, Enum):
    CONTINUE = "CONTINUE"
    RECOVER = "RECOVER"
    DISENGAGE = "DISENGAGE"


@dataclass(frozen=True)
class SurvivalVerdict:
    action: SurvivalAction
    time_to_death_s: float | None
    dynamic_threshold: float | None
    velocity: VelocityEstimate
    source: DataSource
    reason: str

    @property
    def preempts_combat(self) -> bool:
        return self.action is not SurvivalAction.CONTINUE


class BurstMonitor:
    r"""Detects burst damage from the HP *trend* rather than a fixed threshold.

    A static "heal below 30%" rule is calibrated for one damage rate and wrong
    for every other: it panics during a slow grind and reacts far too late to a
    burst that crosses 30% and 0% inside a single reaction window. The slope
    carries that information.

    Over the retained window the slope is the ordinary least-squares estimate

    .. math:: \dot h = \frac{\sum_i (t_i - \bar t)(h_i - \bar h)}{\sum_i (t_i - \bar t)^2}

    - a fit rather than a two-point difference, because perception noise on a
    single HP reading is large enough to fabricate a burst on its own.

    Time to death follows as :math:`t_{ttd} = h / (-\dot h)` for :math:`\dot h < 0`,
    which inverts into the threshold this class exists to provide:

    .. math:: h^{*} = -\dot h \cdot t_{react}

    - the HP ratio at which the remaining life equals the reaction budget. It
    rises automatically as incoming damage rises, which is what "dynamic" means
    here.
    """

    def __init__(
        self,
        window_s: float = 3.0,
        capacity: int = 32,
        absolute_floor: float = DEFAULT_ABSOLUTE_FLOOR,
    ) -> None:
        if window_s <= 0.0:
            raise ValueError("window_s must be positive")
        if capacity < 2:
            raise ValueError("capacity must retain at least two samples")
        if not 0.0 <= absolute_floor <= 1.0:
            raise ValueError("absolute_floor must lie in [0, 1]")
        self.window_s = window_s
        self.absolute_floor = absolute_floor
        self._samples: deque[tuple[float, float]] = deque(maxlen=capacity)

    def observe(self, at: float, hp_ratio: float | None) -> None:
        """Record one HP reading. ``None`` is discarded, never interpolated.

        A frame perception could not read is absence of evidence. Carrying the
        previous value forward would flatten the fitted slope at exactly the
        moment the character is being burst down and vision is struggling.
        """
        if hp_ratio is None:
            return
        self._samples.append((at, min(1.0, max(0.0, hp_ratio))))
        while len(self._samples) > 1 and at - self._samples[0][0] > self.window_s:
            self._samples.popleft()

    def reset(self) -> None:
        self._samples.clear()

    def velocity(self) -> VelocityEstimate:
        samples = tuple(self._samples)
        if len(samples) < 2:
            return VelocityEstimate(
                slope_per_s=None,
                samples=len(samples),
                span_s=0.0,
                source=DataSource.UNKNOWN,
                reason="fewer than two HP samples in the window",
            )

        mean_t = sum(t for t, _ in samples) / len(samples)
        mean_h = sum(h for _, h in samples) / len(samples)
        variance = sum((t - mean_t) ** 2 for t, _ in samples)
        span = samples[-1][0] - samples[0][0]
        if variance <= 0.0:
            # Every sample carries the same timestamp, so no rate is defined.
            return VelocityEstimate(
                slope_per_s=None,
                samples=len(samples),
                span_s=span,
                source=DataSource.UNKNOWN,
                reason="samples share one timestamp; no slope is defined",
            )

        covariance = sum((t - mean_t) * (h - mean_h) for t, h in samples)
        return VelocityEstimate(
            slope_per_s=covariance / variance,
            samples=len(samples),
            span_s=span,
            source=DataSource.DERIVED,
            reason=f"least squares over {len(samples)} samples spanning {span:.3f}s",
        )

    def verdict(
        self,
        hp_ratio: float | None,
        recover_budget_s: float,
        escape_budget_s: float,
    ) -> SurvivalVerdict:
        """Decide whether combat may continue.

        ``escape_budget_s`` is the shorter horizon: once projected life is that
        brief, healing cannot outpace the incoming rate and only breaking
        contact changes the outcome. ``recover_budget_s`` is the longer one, in
        which a potion still lands in time.
        """
        if escape_budget_s <= 0.0 or recover_budget_s <= 0.0:
            raise ValueError("budgets must be positive")
        if escape_budget_s > recover_budget_s:
            raise ValueError("escape_budget_s must not exceed recover_budget_s")

        velocity = self.velocity()

        if hp_ratio is None:
            # No ratio, no scale. Absolute HP cannot stand in for it - the
            # UNKNOWN_SURVIVAL precedent in nosai.core.tactical_ranking exists
            # because 200 HP reads as healthy against every threshold tuned for
            # a 0..100 bar. Fail toward self-preservation: attempting a recovery
            # that was not needed costs one decision frame, and skipping one
            # that was costs the character.
            return SurvivalVerdict(
                action=SurvivalAction.RECOVER,
                time_to_death_s=None,
                dynamic_threshold=None,
                velocity=velocity,
                source=DataSource.UNKNOWN,
                reason="HP ratio unobservable; failing closed to recovery",
            )

        slope = velocity.slope_per_s
        if slope is None or slope >= 0.0:
            # No measurable decline. The floor is all that remains, and it is
            # reported as the backstop it is rather than as a trend reading.
            if hp_ratio <= self.absolute_floor:
                return SurvivalVerdict(
                    action=SurvivalAction.RECOVER,
                    time_to_death_s=None,
                    dynamic_threshold=self.absolute_floor,
                    velocity=velocity,
                    source=DataSource.DERIVED,
                    reason=(
                        f"HP {hp_ratio:.3f} at or below the absolute floor "
                        f"{self.absolute_floor:.3f}; no decline measurable"
                    ),
                )
            return SurvivalVerdict(
                action=SurvivalAction.CONTINUE,
                time_to_death_s=None,
                dynamic_threshold=self.absolute_floor,
                velocity=velocity,
                source=DataSource.DERIVED,
                reason=f"no measurable HP decline ({velocity.reason})",
            )

        drain = -slope
        time_to_death = hp_ratio / drain if drain > 0.0 else math.inf
        threshold = drain * recover_budget_s

        if time_to_death <= escape_budget_s:
            return SurvivalVerdict(
                action=SurvivalAction.DISENGAGE,
                time_to_death_s=time_to_death,
                dynamic_threshold=drain * escape_budget_s,
                velocity=velocity,
                source=DataSource.DERIVED,
                reason=(
                    f"burst detected: {time_to_death:.3f}s of life at {drain:.4f}/s "
                    f"is inside the {escape_budget_s:.3f}s escape budget"
                ),
            )
        if time_to_death <= recover_budget_s or hp_ratio <= self.absolute_floor:
            return SurvivalVerdict(
                action=SurvivalAction.RECOVER,
                time_to_death_s=time_to_death,
                dynamic_threshold=threshold,
                velocity=velocity,
                source=DataSource.DERIVED,
                reason=(
                    f"{time_to_death:.3f}s of life at {drain:.4f}/s; dynamic threshold "
                    f"{threshold:.3f} against HP {hp_ratio:.3f}"
                ),
            )
        return SurvivalVerdict(
            action=SurvivalAction.CONTINUE,
            time_to_death_s=time_to_death,
            dynamic_threshold=threshold,
            velocity=velocity,
            source=DataSource.DERIVED,
            reason=(
                f"{time_to_death:.3f}s of life exceeds the {recover_budget_s:.3f}s "
                f"recovery budget"
            ),
        )
