"""Tier A - predictive cooldown, cast and interruption model.

The shadow clock answers what perception cannot: *is this action off cooldown
right now?* Vision drops frames, and a dropped frame is not evidence that an
action became available. The clock therefore predicts from the one event the
runtime timestamps exactly - the moment it asked for an action - and degrades
the provenance of that prediction as it ages, instead of presenting a stale
guess as a live reading.

Nothing here executes. The clock is told about executions; it never causes one.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

from nosai.core.data_classification import DataSource

from .action_model import ActionBook, ActionSpec

# Default grace period before an un-refreshed prediction stops being reported as
# DERIVED. Two seconds is roughly three perception frames at the rates this
# runtime targets: long enough to ride out a dropped frame, short enough that a
# stalled perception pipeline becomes visible rather than silently trusted.
DEFAULT_CONFIRMATION_HORIZON_S = 2.0


class ReadinessState(str, Enum):
    READY = "READY"
    COOLING = "COOLING"
    UNKNOWN = "UNKNOWN"


@dataclass(frozen=True)
class Readiness:
    """Availability of one action, with the provenance of that claim.

    ``remaining_s`` is ``None`` whenever the end of the cooldown is genuinely
    unknown - notably after perception reports an action as cooling that the
    clock believed was ready. Zero would assert readiness; ``None`` asserts
    nothing.
    """

    action_id: str
    state: ReadinessState
    remaining_s: float | None
    source: DataSource
    information_age_s: float | None
    reason: str

    @property
    def is_ready(self) -> bool:
        return self.state is ReadinessState.READY


@dataclass(frozen=True)
class ClockDiagnostics:
    action_id: str
    reconciliations: int
    total_absolute_drift_s: float
    last_drift_s: float | None
    unconfirmed_executions: int

    @property
    def mean_absolute_drift_s(self) -> float | None:
        if self.reconciliations == 0:
            return None
        return self.total_absolute_drift_s / self.reconciliations


@dataclass
class _Entry:
    ready_at: float | None = None
    fired_at: float | None = None
    confirmed_at: float | None = None
    cooling_end_unknown: bool = False
    reconciliations: int = 0
    total_absolute_drift_s: float = 0.0
    last_drift_s: float | None = None
    unconfirmed_executions: int = 0

    @property
    def last_information_at(self) -> float | None:
        stamps = [s for s in (self.fired_at, self.confirmed_at) if s is not None]
        return max(stamps) if stamps else None


class ShadowCooldownClock:
    r"""Per-action cooldown estimator that survives missed perception frames.

    Availability of action :math:`a_i` fired at :math:`t_f` is predicted as
    :math:`t_c(a_i) = t_f + \mathrm{cooldown}(a_i)`, giving
    :math:`\mathrm{remaining}(t) = \max(0,\; t_c - t)`.

    Provenance, not the estimate, is what ages:

    * ``DERIVED``  - rests on an execution or confirmation no older than the
      confirmation horizon;
    * ``CACHED``   - rests on older information, reported with its age;
    * ``UNKNOWN``  - no execution and no observation exists for the action.

    A ``CACHED`` *READY* verdict stays safe while a ``CACHED`` *COOLING* one is
    merely conservative. Only this runtime fires its own actions, so the single
    way a long-elapsed cooldown could still be running is that some fire went
    unrecorded - and an unrecorded fire can only make the true state *more*
    ready than predicted, never less. Under-reporting readiness costs uptime;
    over-reporting it would desynchronise the model from the client.
    """

    def __init__(
        self,
        book: ActionBook,
        confirmation_horizon_s: float = DEFAULT_CONFIRMATION_HORIZON_S,
    ) -> None:
        if confirmation_horizon_s <= 0.0:
            raise ValueError("confirmation_horizon_s must be positive")
        self.book = book
        self.confirmation_horizon_s = confirmation_horizon_s
        self._entries: dict[str, _Entry] = {}

    def _entry(self, action_id: str) -> _Entry:
        # Raises for an unregistered id; see ActionBook on why that stays loud.
        self.book[action_id]
        return self._entries.setdefault(action_id, _Entry())

    def note_execution(self, action_id: str, at: float) -> None:
        """Record that the runtime asked for ``action_id`` at time ``at``.

        This is a request, not a landing. ``confirmed_at`` stays untouched so a
        request perception never corroborates ages into ``CACHED`` and says so.
        """
        spec = self.book[action_id]
        entry = self._entries.setdefault(action_id, _Entry())
        entry.fired_at = at
        entry.ready_at = at + spec.cooldown_s
        entry.cooling_end_unknown = False
        entry.unconfirmed_executions += 1

    def reconcile(self, action_id: str, observed_ready: bool, at: float) -> float | None:
        """Fold a real observation of the client's cooldown display into the model.

        Returns the signed drift in seconds (prediction minus observation) when
        it is measurable, else ``None``. Positive drift means the clock ran long:
        it predicted a cooldown that had in fact already expired.
        """
        entry = self._entry(action_id)
        drift: float | None = None

        if observed_ready:
            if entry.ready_at is not None and entry.ready_at > at:
                drift = entry.ready_at - at
            entry.ready_at = at
            entry.cooling_end_unknown = False
        else:
            predicted_ready = entry.ready_at is not None and entry.ready_at <= at
            if predicted_ready:
                # Still cooling past the predicted end. How much longer is not
                # observable from a greyed-out icon, so no end is invented: the
                # entry moves to an explicitly unbounded cooling state and
                # ``remaining_s`` becomes None rather than zero.
                drift = at - float(entry.ready_at)
                entry.ready_at = None
                entry.cooling_end_unknown = True
            elif entry.ready_at is None:
                entry.cooling_end_unknown = True

        entry.confirmed_at = at
        entry.unconfirmed_executions = 0
        if drift is not None:
            entry.reconciliations += 1
            entry.total_absolute_drift_s += abs(drift)
            entry.last_drift_s = drift
        return drift

    def readiness(self, action_id: str, now: float) -> Readiness:
        spec = self.book[action_id]
        entry = self._entries.get(action_id)

        if entry is None or entry.last_information_at is None:
            # Never fired, never seen. Not ready, not cooling - unknown.
            return Readiness(
                action_id=action_id,
                state=ReadinessState.UNKNOWN,
                remaining_s=None,
                source=DataSource.UNKNOWN,
                information_age_s=None,
                reason="no execution or observation on record",
            )

        age = max(0.0, now - float(entry.last_information_at))
        fresh = age <= self.confirmation_horizon_s
        source = DataSource.DERIVED if fresh else DataSource.CACHED
        notes: list[str] = []
        if not fresh:
            notes.append(f"information stale by {age - self.confirmation_horizon_s:.3f}s")
            if entry.unconfirmed_executions:
                notes.append(f"{entry.unconfirmed_executions} execution(s) unconfirmed by perception")

        if entry.cooling_end_unknown:
            notes.insert(0, "observed cooling with unobservable end")
            return Readiness(
                action_id=action_id,
                state=ReadinessState.COOLING,
                remaining_s=None,
                source=source,
                information_age_s=age,
                reason="; ".join(notes),
            )

        ready_at = entry.ready_at
        if ready_at is not None and now < ready_at:
            notes.insert(
                0, f"shadow clock: {ready_at - now:.3f}s remaining of {spec.cooldown_s:.3f}s"
            )
            return Readiness(
                action_id=action_id,
                state=ReadinessState.COOLING,
                remaining_s=ready_at - now,
                source=source,
                information_age_s=age,
                reason="; ".join(notes),
            )

        notes.insert(0, "shadow clock: cooldown elapsed")
        return Readiness(
            action_id=action_id,
            state=ReadinessState.READY,
            remaining_s=0.0,
            source=source,
            information_age_s=age,
            reason="; ".join(notes),
        )

    def snapshot(self, now: float) -> tuple[Readiness, ...]:
        return tuple(self.readiness(action_id, now) for action_id in self.book.ids)

    def diagnostics(self, action_id: str) -> ClockDiagnostics:
        entry = self._entry(action_id)
        return ClockDiagnostics(
            action_id=action_id,
            reconciliations=entry.reconciliations,
            total_absolute_drift_s=entry.total_absolute_drift_s,
            last_drift_s=entry.last_drift_s,
            unconfirmed_executions=entry.unconfirmed_executions,
        )


class CastPhase(str, Enum):
    CAST = "CAST"
    ANIMATION_LOCK = "ANIMATION_LOCK"
    FREE = "FREE"


@dataclass(frozen=True)
class CastWindow:
    """The occupancy an in-flight action imposes on the decision loop."""

    action_id: str
    starts_at: float
    cast_ends_at: float
    lock_ends_at: float

    @staticmethod
    def for_action(spec: ActionSpec, starts_at: float) -> "CastWindow":
        cast_ends = starts_at + spec.cast_s
        return CastWindow(spec.action_id, starts_at, cast_ends, cast_ends + spec.animation_lock_s)

    def phase_at(self, now: float) -> CastPhase:
        if now < self.cast_ends_at:
            return CastPhase.CAST
        if now < self.lock_ends_at:
            return CastPhase.ANIMATION_LOCK
        return CastPhase.FREE


@dataclass(frozen=True)
class PredictedTransition:
    """An enemy state change forecast to land at ``at``.

    ``interrupt_probability`` is the chance it breaks a cast in progress.
    """

    at: float
    interrupt_probability: float
    label: str = ""

    def __post_init__(self) -> None:
        if not 0.0 <= self.interrupt_probability <= 1.0:
            raise ValueError("interrupt_probability must lie in [0, 1]")


@dataclass(frozen=True)
class InterruptForecast:
    action_id: str
    interrupt_probability: float
    completion_probability: float
    expected_value_multiplier: float
    first_threat_at: float | None
    contributing: tuple[str, ...]
    reason: str


@dataclass(frozen=True)
class CancelVerdict:
    cancel: bool
    phase: CastPhase
    reason: str


class InterruptModel:
    """Scores whether a cast will survive the transitions forecast against it."""

    def __init__(self, book: ActionBook, loss_on_interrupt: float = 1.0) -> None:
        if loss_on_interrupt < 0.0:
            raise ValueError("loss_on_interrupt must be non-negative")
        self.book = book
        self.loss_on_interrupt = loss_on_interrupt

    def forecast(
        self,
        action_id: str,
        start_at: float,
        transitions: tuple[PredictedTransition, ...] = (),
    ) -> InterruptForecast:
        r"""Probability that a cast started at ``start_at`` completes.

        Transitions are treated as independent, so the chance that at least one
        interrupt lands inside the cast window is

        .. math:: P = 1 - \prod_{i \in W}(1 - p_i),
           \qquad W = \{i : t_0 \le t_i < t_0 + t_{cast}\}

        Expected yield relative to an uncontested cast is

        .. math:: m = (1 - P) - P\lambda

        where :math:`\lambda` is the cooldown lost when an interrupt still
        consumes it and zero otherwise. The engine multiplies the action's value
        by :math:`m`, so an action that is both fragile and expensive to waste
        falls below a weaker instant one.
        """
        spec = self.book[action_id]
        if spec.cast_s <= 0.0:
            # Instant actions expose no window in which to be interrupted.
            return InterruptForecast(
                action_id=action_id,
                interrupt_probability=0.0,
                completion_probability=1.0,
                expected_value_multiplier=1.0,
                first_threat_at=None,
                contributing=(),
                reason="instant action: no cast window to interrupt",
            )

        cast_ends_at = start_at + spec.cast_s
        window = sorted(
            (t for t in transitions if start_at <= t.at < cast_ends_at),
            key=lambda t: (t.at, t.label),
        )
        survival = 1.0
        for transition in window:
            survival *= 1.0 - transition.interrupt_probability
        interrupt_probability = 1.0 - survival

        penalty = self.loss_on_interrupt if spec.consumes_cooldown_on_interrupt else 0.0
        multiplier = survival - interrupt_probability * penalty

        return InterruptForecast(
            action_id=action_id,
            interrupt_probability=interrupt_probability,
            completion_probability=survival,
            expected_value_multiplier=multiplier,
            first_threat_at=window[0].at if window else None,
            contributing=tuple(t.label or f"t={t.at:.3f}" for t in window),
            reason=(
                f"{len(window)} transition(s) inside a {spec.cast_s:.3f}s cast"
                if window
                else f"no transition forecast inside the {spec.cast_s:.3f}s cast window"
            ),
        )

    def should_cancel_cast(
        self,
        window: CastWindow,
        now: float,
        alternative_value: float,
        transitions: tuple[PredictedTransition, ...] = (),
        forced: bool = False,
    ) -> CancelVerdict:
        r"""Whether to abandon a cast still in progress.

        Cancelling here destroys the payload, so the residual value of finishing
        is :math:`V_{res} = m(t)\,V(a)` with the completion probability
        recomputed over the *remaining* window only. Cancel iff the alternative
        strictly beats it, or iff survival forced the question.
        """
        spec = self.book[window.action_id]
        phase = window.phase_at(now)
        if phase is not CastPhase.CAST:
            return CancelVerdict(False, phase, "not in a cast phase")
        if not spec.cancellable:
            return CancelVerdict(False, phase, f"{spec.action_id} is not cancellable")
        if forced:
            return CancelVerdict(True, phase, "survival failsafe forced the cancel")

        remaining = tuple(t for t in transitions if now <= t.at < window.cast_ends_at)
        survival = 1.0
        for transition in remaining:
            survival *= 1.0 - transition.interrupt_probability
        residual = survival * spec.damage_ratio

        if alternative_value > residual:
            return CancelVerdict(
                True,
                phase,
                f"alternative {alternative_value:.4f} beats residual {residual:.4f} "
                f"(completion p={survival:.3f})",
            )
        return CancelVerdict(
            False,
            phase,
            f"residual {residual:.4f} holds against alternative {alternative_value:.4f}",
        )

    def should_cancel_animation(
        self,
        window: CastWindow,
        now: float,
        has_queued_action: bool,
    ) -> CancelVerdict:
        """Whether to cut the post-cast recovery animation short.

        Distinct from a cast cancel: past ``cast_ends_at`` the payload has
        already resolved, so cancelling costs nothing and only returns time to
        the decision loop. It is therefore worth doing whenever anything is
        waiting to act.
        """
        spec = self.book[window.action_id]
        phase = window.phase_at(now)
        if phase is not CastPhase.ANIMATION_LOCK:
            return CancelVerdict(False, phase, "not in an animation lock")
        if not spec.cancellable:
            return CancelVerdict(False, phase, f"{spec.action_id} is not cancellable")
        if not has_queued_action:
            return CancelVerdict(False, phase, "nothing queued; cancelling frees no time")
        freed = window.lock_ends_at - now
        return CancelVerdict(True, phase, f"payload resolved; cancel frees {freed:.3f}s")
