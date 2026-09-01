"""Stochastic predictive combat engine composing the three tactical tiers.

The engine is a Decision/Policy component and nothing more. It proposes a single
:class:`~nosai.core.contracts.CandidateAction` per frame and returns it for the
SafetyGate to accept or reject, exactly as ``PlayAiEngine`` does. It performs no
client I/O, sends no input, and holds no executor; the shadow clock learns about
executions because it is *told* about them through :meth:`note_execution`, after
something downstream has actually acted.

Tier order within a frame is fixed and is itself a safety property:

1. **Survival** (Tier C failsafe) - answered first, so a burst that will end the
   character cannot be out-voted by a high-value attack. This mirrors
   ``ActionPriority.CRITICAL_SURVIVAL`` in ``nosai.core.tactical_ranking``.
2. **Threat weighting** (Tier C) - which target the fight should be about.
3. **Availability and interruption** (Tier A) - which actions can be committed
   to at all.
4. **Search** (Tier B) - which of the survivors maximises the value function.

A tier can only ever narrow what the tiers below it may choose from.
"""
from __future__ import annotations

from dataclasses import dataclass, field, replace

from nosai.core.contracts import (
    ActionType,
    CandidateAction,
    Decision,
    Goal,
    WorldState,
)
from nosai.core.data_classification import DataSource

from .action_model import ActionBook
from .scheduling import (
    CancelVerdict,
    CastWindow,
    InterruptForecast,
    InterruptModel,
    PredictedTransition,
    Readiness,
    ReadinessState,
    ShadowCooldownClock,
)
from .search import CombatSimState, MonteCarloCombatSearch, SearchConfig, SearchResult
from .stochastic import StochasticTransitionMatrix
from .threat import (
    BurstMonitor,
    SurvivalAction,
    SurvivalVerdict,
    ThreatCandidate,
    ThreatEvaluator,
    ThreatScore,
    ThreatWeights,
)

# Time a recovery item needs to land: cast plus input latency plus a margin.
# The burst detector compares projected life against this, so it is a property
# of the client and the link, not a tuning knob for aggression.
DEFAULT_RECOVER_BUDGET_S = 1.2

# Below this much projected life, healing no longer outpaces the incoming rate
# and only breaking contact changes the outcome.
DEFAULT_ESCAPE_BUDGET_S = 0.5

# ``WorldState.target_hp`` is fed from ``PerceptionSnapshot.target_hp_pct`` by
# ``PerceptionWorldAdapter``, so its scale is a percentage by contract rather
# than by assumption. Named here so the coupling is greppable if that ever moves.
TARGET_HP_PCT_SCALE = 100.0


def _ratio(value: float | None, maximum: float | None) -> float | None:
    if value is None or maximum is None or maximum <= 0.0:
        return None
    return min(1.0, max(0.0, value / maximum))


@dataclass(frozen=True)
class CombatObservation:
    """Everything one decision frame is allowed to rest on.

    Time is injected rather than read from a clock, so a frame replays
    identically in a test, in a simulation and in a post-mortem.
    """

    now: float
    world: WorldState
    target_class: str | None = None
    threats: tuple[ThreatCandidate, ...] = ()
    predicted_transitions: tuple[PredictedTransition, ...] = ()
    incoming_dps: float | None = None
    active_cast: CastWindow | None = None


@dataclass(frozen=True)
class CombatFrame:
    """The full reasoning behind one proposal, not just its conclusion."""

    decision: Decision
    survival: SurvivalVerdict
    threats: tuple[ThreatScore, ...] = ()
    retarget_to: object | None = None
    readiness: tuple[Readiness, ...] = ()
    search: SearchResult | None = None
    interrupt: InterruptForecast | None = None
    cancel: CancelVerdict | None = None
    risk_source: DataSource = DataSource.UNKNOWN
    withheld: tuple[tuple[str, str], ...] = field(default_factory=tuple)


class StochasticCombatEngine:
    """Three-tier predictive combat policy. Proposes; never executes."""

    def __init__(
        self,
        book: ActionBook,
        matrix: StochasticTransitionMatrix | None = None,
        clock: ShadowCooldownClock | None = None,
        interrupt_model: InterruptModel | None = None,
        monitor: BurstMonitor | None = None,
        evaluator: ThreatEvaluator | None = None,
        search_config: SearchConfig | None = None,
        threat_weights: ThreatWeights | None = None,
        recovery_action_id: str | None = None,
        recover_budget_s: float = DEFAULT_RECOVER_BUDGET_S,
        escape_budget_s: float = DEFAULT_ESCAPE_BUDGET_S,
    ) -> None:
        if recovery_action_id is not None and recovery_action_id not in book:
            raise ValueError(f"recovery_action_id {recovery_action_id!r} is not in the action book")
        self.book = book
        self.matrix = matrix or StochasticTransitionMatrix(book)
        self.clock = clock or ShadowCooldownClock(book)
        self.interrupt_model = interrupt_model or InterruptModel(book)
        self.monitor = monitor or BurstMonitor()
        self.evaluator = evaluator or ThreatEvaluator(threat_weights)
        self.search_config = search_config or SearchConfig()
        self.search = MonteCarloCombatSearch(book, self.matrix, self.search_config)
        # Variant used when the target's HP cannot be read. Killing blows are
        # worth nothing a search can bank on if it does not know how close the
        # target is to dying, so the bonus is removed rather than awarded against
        # a placeholder level. Prebuilt so no frame pays to construct it.
        self._search_unknown_target = MonteCarloCombatSearch(
            book,
            self.matrix,
            replace(
                self.search_config,
                weights=replace(self.search_config.weights, kill_bonus=0.0),
            ),
        )
        self.recovery_action_id = recovery_action_id
        self.recover_budget_s = recover_budget_s
        self.escape_budget_s = escape_budget_s
        self._last_observed_at: float | None = None
        self._last_frame: CombatFrame | None = None
        self._last_tick_id: int | None = None

    # ------------------------------------------------------------- learning

    def note_execution(self, action_id: str, at: float) -> None:
        """Tell the shadow clock an action was requested. Called by the executor path."""
        self.clock.note_execution(action_id, at)

    def reconcile_cooldown(self, action_id: str, observed_ready: bool, at: float) -> float | None:
        """Fold a perceived cooldown state into the shadow clock; returns the drift."""
        return self.clock.reconcile(action_id, observed_ready, at)

    def note_effect_outcome(
        self,
        action_id: str,
        target_class: str | None,
        applied: bool,
        weight: float = 1.0,
    ) -> None:
        """Close the learning loop after an execution whose effect was *observed*.

        Only call this when the target's status was actually read. An effect
        that could not be observed is unknown, and recording it as a resist
        teaches the matrix the action does not work.
        """
        self.matrix.observe(action_id, target_class, applied, weight)

    # ------------------------------------------------------------ evaluation

    def evaluate(self, observation: CombatObservation) -> CombatFrame:
        world = observation.world
        now = observation.now
        hp_ratio = world.hp_ratio()

        # Idempotent for a repeated frame: re-evaluating the same instant must
        # not enter the same HP reading twice and halve the fitted slope.
        if self._last_observed_at is None or now > self._last_observed_at:
            self.monitor.observe(now, hp_ratio)
            self._last_observed_at = now

        readiness = self.clock.snapshot(now)
        survival = self.monitor.verdict(hp_ratio, self.recover_budget_s, self.escape_budget_s)
        threats = self.evaluator.prioritise(observation.threats)
        retarget = self._retarget_recommendation(threats, world)

        if survival.preempts_combat:
            frame = self._survival_frame(observation, survival, threats, retarget, readiness)
            self._remember(frame, world)
            return frame

        if world.target_id is None:
            frame = CombatFrame(
                decision=Decision(
                    action=CandidateAction(ActionType.NOOP),
                    confidence=1.0,
                    reasoning="no observed target; nothing offensive is proposable",
                ),
                survival=survival,
                threats=threats,
                retarget_to=retarget,
                readiness=readiness,
            )
            self._remember(frame, world)
            return frame

        allowed, withheld = self._commitable_actions(readiness, world, now, observation)
        mp_ratio = _ratio(world.mp, world.max_mp)
        incoming, risk_source = self._incoming_dps(observation, survival)
        target_ratio = (
            None if world.target_hp is None else _ratio(world.target_hp, TARGET_HP_PCT_SCALE)
        )

        root = CombatSimState(
            t=now,
            # Reached only when the survival tier confirmed a readable ratio.
            own_hp=hp_ratio if hp_ratio is not None else 0.0,
            # An unreadable MP bar leaves affordability unknowable, so the root
            # is pinned to zero and ``allowed`` already holds only free actions.
            # Both halves of that pairing are required: a fabricated full bar
            # would let the search plan a rotation the character cannot pay for.
            own_mp=mp_ratio if mp_ratio is not None else 0.0,
            # An unreadable target HP is not a full one. The placeholder level
            # below feeds only the delta terms, which are level-independent; the
            # kill bonus and the "target is dead" terminal are the two things
            # that would read meaning into it, and the search chosen on the next
            # line has the first disabled and can therefore never reach the
            # second.
            target_hp=target_ratio if target_ratio is not None else 1.0,
            incoming_dps=incoming,
            cooldowns=self._sim_cooldowns(readiness),
        )
        search = self.search if target_ratio is not None else self._search_unknown_target

        result = search.search(
            root_state=root,
            target_class=observation.target_class,
            # Seeded from the tick, so the whole search is a pure function of the
            # observed state and reproduces exactly in replay.
            seed=world.tick_id,
            restrict_to=allowed,
        )

        frame = self._combat_frame(
            observation,
            survival,
            threats,
            retarget,
            readiness,
            result,
            risk_source,
            withheld,
            target_hp_known=target_ratio is not None,
        )
        self._remember(frame, world)
        return frame

    # ------------------------------------------------------- DecisionProvider

    def decide(self, world_state: WorldState, goal: Goal) -> Decision:
        """``DecisionProvider`` adapter, so the engine drops into ``NosAiOrchestrator``.

        The protocol carries no timestamp and no threat field, so this returns
        the proposal from the frame evaluated for *this* tick. A tick that was
        never evaluated yields NOOP rather than a decision built on the previous
        tick's world - a stale proposal is indistinguishable from a current one
        once it reaches the SafetyGate.
        """
        del goal
        if self._last_frame is None or self._last_tick_id != world_state.tick_id:
            return Decision(
                action=CandidateAction(ActionType.NOOP),
                confidence=1.0,
                reasoning=(
                    f"no combat frame evaluated for tick {world_state.tick_id}; "
                    "call evaluate() with a CombatObservation first"
                ),
            )
        return self._last_frame.decision

    @property
    def last_frame(self) -> CombatFrame | None:
        return self._last_frame

    # ---------------------------------------------------------------- internals

    def _remember(self, frame: CombatFrame, world: WorldState) -> None:
        self._last_frame = frame
        self._last_tick_id = world.tick_id

    def _retarget_recommendation(
        self, threats: tuple[ThreatScore, ...], world: WorldState
    ) -> object | None:
        """Top-priority entity when it differs from the observed target.

        Deliberately a recommendation and not an action. ``ActionType`` has no
        member for acquiring a target, and the SafetyGate rejects any offensive
        action whose target does not match ``world.target_id``, so an engine that
        "switched" targets on its own would emit proposals guaranteed to be
        blocked. Selecting a target is a contract change and needs an ADR; until
        then the runtime is told what the tier concluded and decides for itself.
        """
        for score in threats:
            if score.priority is None:
                continue
            if world.target_id is not None and score.entity_id == world.target_id:
                return None
            return score.entity_id
        return None

    def _commitable_actions(
        self,
        readiness: tuple[Readiness, ...],
        world: WorldState,
        now: float,
        observation: CombatObservation,
    ) -> tuple[tuple[str, ...], tuple[tuple[str, str], ...]]:
        """Actions Tier A will let Tier B consider, and why the rest were withheld.

        Readiness that is ``UNKNOWN`` withholds an offensive action but not a
        survival one. The asymmetry is the same one the burst monitor makes: a
        wasted offensive frame costs uptime and desynchronises the shadow clock,
        while a withheld recovery costs the character. ``nosai.core.tactical_ranking``
        already resolved this direction with ``ActionPriority.UNKNOWN_SURVIVAL``.
        """
        mp_ratio = _ratio(world.mp, world.max_mp)
        allowed: list[str] = []
        withheld: list[tuple[str, str]] = []

        for state in readiness:
            spec = self.book[state.action_id]
            if state.state is ReadinessState.COOLING:
                withheld.append((spec.action_id, f"cooling: {state.reason}"))
                continue
            if state.state is ReadinessState.UNKNOWN and not spec.is_survival:
                withheld.append((spec.action_id, "readiness unknown; offensive action withheld"))
                continue
            if spec.mp_ratio_cost > 0.0:
                if mp_ratio is None:
                    withheld.append((spec.action_id, "MP ratio unobservable; costed action withheld"))
                    continue
                if spec.mp_ratio_cost > mp_ratio:
                    withheld.append(
                        (spec.action_id, f"costs {spec.mp_ratio_cost:.3f} MP against {mp_ratio:.3f}")
                    )
                    continue
            forecast = self.interrupt_model.forecast(
                spec.action_id, now, observation.predicted_transitions
            )
            if forecast.expected_value_multiplier <= 0.0:
                withheld.append(
                    (
                        spec.action_id,
                        f"interrupt-adjusted yield {forecast.expected_value_multiplier:.3f} "
                        f"is not positive: {forecast.reason}",
                    )
                )
                continue
            allowed.append(spec.action_id)

        return tuple(allowed), tuple(withheld)

    def _sim_cooldowns(self, readiness: tuple[Readiness, ...]) -> tuple[tuple[str, float], ...]:
        """Cooldowns the forward model starts from.

        A cooling action with an unobservable end is seeded at its full nominal
        cooldown. That is the conservative reading: the alternative, treating an
        unknown remainder as zero, would have the search plan around an action it
        cannot fire.
        """
        pairs: list[tuple[str, float]] = []
        for state in readiness:
            if state.state is not ReadinessState.COOLING:
                continue
            remaining = state.remaining_s
            if remaining is None:
                remaining = self.book[state.action_id].cooldown_s
            if remaining > 0.0:
                pairs.append((state.action_id, remaining))
        return tuple(sorted(pairs))

    def _incoming_dps(
        self, observation: CombatObservation, survival: SurvivalVerdict
    ) -> tuple[float, DataSource]:
        if observation.incoming_dps is not None:
            return max(0.0, observation.incoming_dps), DataSource.LIVE
        slope = survival.velocity.slope_per_s
        if slope is not None and slope < 0.0:
            return -slope, DataSource.DERIVED
        # Nothing measurable. Zero is the only rate that invents no damage; the
        # frame reports UNKNOWN so a caller can see the risk term was inert
        # rather than concluding the fight was safe.
        return 0.0, DataSource.UNKNOWN

    def _survival_frame(
        self,
        observation: CombatObservation,
        survival: SurvivalVerdict,
        threats: tuple[ThreatScore, ...],
        retarget: object | None,
        readiness: tuple[Readiness, ...],
    ) -> CombatFrame:
        cancel: CancelVerdict | None = None
        if observation.active_cast is not None:
            cancel = self.interrupt_model.should_cancel_cast(
                observation.active_cast,
                observation.now,
                alternative_value=0.0,
                transitions=observation.predicted_transitions,
                forced=True,
            )

        if survival.action is SurvivalAction.DISENGAGE:
            action = CandidateAction(
                ActionType.MOVE,
                parameters={"intent": "disengage", "time_to_death_s": survival.time_to_death_s},
            )
        else:
            parameters: dict[str, object] = {"intent": "recover"}
            if self.recovery_action_id is not None:
                parameters["action_id"] = self.recovery_action_id
            action = CandidateAction(ActionType.RECOVER, parameters=parameters)

        return CombatFrame(
            decision=Decision(
                action=action,
                # The policy under an unreadable state is not uncertain - it is
                # fixed. Which state produced it lives in the reasoning and in
                # ``survival.source``, where provenance belongs.
                confidence=1.0,
                reasoning=f"survival failsafe [{survival.source.value}]: {survival.reason}",
            ),
            survival=survival,
            threats=threats,
            retarget_to=retarget,
            readiness=readiness,
            cancel=cancel,
            risk_source=survival.velocity.source,
        )

    def _combat_frame(
        self,
        observation: CombatObservation,
        survival: SurvivalVerdict,
        threats: tuple[ThreatScore, ...],
        retarget: object | None,
        readiness: tuple[Readiness, ...],
        result: SearchResult,
        risk_source: DataSource,
        withheld: tuple[tuple[str, str], ...],
        target_hp_known: bool,
    ) -> CombatFrame:
        world = observation.world
        target_note = "" if target_hp_known else ", target HP unobserved (kill bonus withheld)"

        if result.action_id is None:
            return CombatFrame(
                decision=Decision(
                    action=CandidateAction(ActionType.NOOP),
                    confidence=1.0,
                    reasoning=f"no action committed: {result.reason}",
                ),
                survival=survival,
                threats=threats,
                retarget_to=retarget,
                readiness=readiness,
                search=result,
                risk_source=risk_source,
                withheld=withheld,
            )

        spec = self.book[result.action_id]
        forecast = self.interrupt_model.forecast(
            spec.action_id, observation.now, observation.predicted_transitions
        )

        cancel: CancelVerdict | None = None
        if observation.active_cast is not None:
            adjusted = result.expected_value * forecast.expected_value_multiplier
            cancel = self.interrupt_model.should_cancel_cast(
                observation.active_cast,
                observation.now,
                alternative_value=adjusted,
                transitions=observation.predicted_transitions,
            )
            if not cancel.cancel:
                animation = self.interrupt_model.should_cancel_animation(
                    observation.active_cast, observation.now, has_queued_action=True
                )
                if animation.cancel:
                    cancel = animation

        needs_target = spec.action_type in {ActionType.ATTACK, ActionType.SKILL}
        action = CandidateAction(
            spec.action_type,
            target_id=world.target_id if needs_target else None,
            parameters={
                "action_id": spec.action_id,
                "expected_value": result.expected_value,
                "interrupt_probability": forecast.interrupt_probability,
                "search_seed": result.seed,
            },
        )

        # Visit share is a bounded, honest confidence: it is the fraction of the
        # budget the search spent convinced this was the line, and it falls as
        # the alternatives close in.
        total_visits = sum(item.visits for item in result.per_action) or 1
        confidence = min(1.0, max(0.0, result.visits / total_visits))

        return CombatFrame(
            decision=Decision(
                action=action,
                confidence=confidence,
                reasoning=(
                    f"{result.reason}; value={result.expected_value:.4f}, "
                    f"completion p={forecast.completion_probability:.3f}, "
                    f"risk source={risk_source.value}{target_note}"
                ),
            ),
            survival=survival,
            threats=threats,
            retarget_to=retarget,
            readiness=readiness,
            search=result,
            interrupt=forecast,
            cancel=cancel,
            risk_source=risk_source,
            withheld=withheld,
        )
