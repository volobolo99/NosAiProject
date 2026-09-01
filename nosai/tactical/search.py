r"""Tier B (search) - depth-limited Monte Carlo tree search over the action tree.

The forward model here is a *simulation*. Its intermediate states never leave
this module and are never published: only the chosen action and its estimated
value escape, and those are ``DERIVED`` - deterministic functions of the trusted
input state, the action book, the posteriors and the declared seed. Feeding a
rollout state back into the World Model would breach the real-vs-simulated rule
in the architecture baseline.

Determinism is a repository invariant, and Monte Carlo is not inherently at odds
with it. Every draw comes from one :class:`random.Random` seeded from the input
state (the engine passes ``WorldState.tick_id``), so the search is a pure
function of ``(state, book, matrix, config, seed)`` and reproduces exactly in
tests and in replay.

The value of a plan is the discounted return

.. math::
   V(s_0) = \sum_{k=0}^{d-1} \Big(\prod_{j<k}\gamma^{\tau_j}\Big)\, r_k,
   \qquad r_k = w_{dmg}\,\Delta h^{tgt}_k \;-\; w_{risk}\,\Delta h^{own}_k

with :math:`\tau_k` the wall time the :math:`k`-th action occupies. Discounting
by elapsed *time* rather than by step is what makes a slow nuke comparable with
two fast strikes; a per-step discount would rate them as if they cost the same.
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass, field

from .action_model import ActionBook, EffectSpec
from .stochastic import StochasticTransitionMatrix

# Internal pseudo-action for "everything is on cooldown". It exists so a depth-4
# search does not truncate to depth 1 whenever the rotation is briefly empty -
# waiting out a cooldown is a real option with a real cost, and pruning it would
# make the engine systematically over-value whatever it can fire immediately.
# It is never proposed: the engine maps an empty plan to NOOP instead.
WAIT_ACTION_ID = "__wait__"


@dataclass(frozen=True)
class SearchWeights:
    """Trade-off between damage dealt and damage taken.

    ``risk`` exceeds ``damage`` because the two are not symmetric: HP spent is
    only recoverable while the character is alive, so an even trade is a losing
    one. ``death_penalty`` then dominates both, keeping any line that ends in a
    death below every line that does not.
    """

    damage: float = 1.0
    risk: float = 1.4
    kill_bonus: float = 0.5
    death_penalty: float = 3.0


@dataclass(frozen=True)
class SearchConfig:
    max_depth: int = 4
    iterations: int = 256
    exploration_c: float = 1.4
    discount_per_second: float = 0.9
    min_step_s: float = 0.2
    max_wait_s: float = 3.0
    weights: SearchWeights = field(default_factory=SearchWeights)

    def __post_init__(self) -> None:
        if self.max_depth < 1:
            raise ValueError("max_depth must be at least 1")
        if self.iterations < 1:
            raise ValueError("iterations must be at least 1")
        if not 0.0 < self.discount_per_second <= 1.0:
            raise ValueError("discount_per_second must lie in (0, 1]")
        if self.min_step_s <= 0.0 or self.max_wait_s <= 0.0:
            raise ValueError("step and wait durations must be positive")


@dataclass(frozen=True)
class CombatSimState:
    """Simulated combat state. All HP/MP figures are bar fractions in [0, 1]."""

    t: float
    own_hp: float
    own_mp: float
    target_hp: float
    incoming_dps: float
    cooldowns: tuple[tuple[str, float], ...] = ()
    effects: tuple[tuple[str, float], ...] = ()

    @property
    def is_terminal(self) -> bool:
        return self.own_hp <= 0.0 or self.target_hp <= 0.0

    def cooldown_of(self, action_id: str) -> float:
        for key, remaining in self.cooldowns:
            if key == action_id:
                return remaining
        return 0.0


def _as_pairs(values: dict[str, float]) -> tuple[tuple[str, float], ...]:
    # Sorted so equivalent states compare and hash identically regardless of the
    # order effects happened to be applied in.
    return tuple(sorted((k, v) for k, v in values.items() if v > 0.0))


class CombatSimulator:
    """Pure forward model: ``(state, action) -> (state', reward, elapsed)``.

    No method reads a clock, touches the client, or mutates its arguments.
    """

    def __init__(
        self,
        book: ActionBook,
        matrix: StochasticTransitionMatrix,
        config: SearchConfig | None = None,
    ) -> None:
        self.book = book
        self.matrix = matrix
        self.config = config or SearchConfig()
        self._effects: dict[str, EffectSpec] = {
            spec.effect.effect_id: spec.effect for spec in book if spec.effect is not None
        }

    def legal_actions(self, state: CombatSimState) -> tuple[str, ...]:
        """Actions off cooldown and affordable, or the wait fallback."""
        ready = tuple(
            spec.action_id
            for spec in self.book
            if state.cooldown_of(spec.action_id) <= 0.0 and spec.mp_ratio_cost <= state.own_mp
        )
        return ready if ready else (WAIT_ACTION_ID,)

    def _scale_at(self, effects: dict[str, float], elapsed_into_step: float, incoming: bool) -> float:
        scale = 1.0
        for effect_id, remaining in effects.items():
            if remaining <= elapsed_into_step:
                continue
            spec = self._effects.get(effect_id)
            if spec is None:
                continue
            scale *= spec.incoming_damage_scale if incoming else spec.outgoing_damage_scale
        return scale

    def _integrate_incoming(
        self, effects: dict[str, float], base_dps: float, elapsed: float
    ) -> float:
        r"""Exact integral of incoming damage across an interval effects expire in.

        The scale is piecewise constant in time, so
        :math:`\int_0^{\tau} \mathrm{dps}\cdot\sigma(u)\,du` is summed over the
        sub-intervals delimited by the expiries. Holding the scale fixed for the
        whole step instead would let a 2 s stun absorb a 3 s action entirely,
        which is exactly the over-valuation that makes a search prefer control
        effects it should not.
        """
        if elapsed <= 0.0 or base_dps <= 0.0:
            return 0.0
        cuts = sorted({0.0, elapsed} | {r for r in effects.values() if 0.0 < r < elapsed})
        total = 0.0
        for start, end in zip(cuts, cuts[1:]):
            midpoint = 0.5 * (start + end)
            total += base_dps * self._scale_at(effects, midpoint, incoming=True) * (end - start)
        return total

    def step(
        self,
        state: CombatSimState,
        action_id: str,
        target_class: str | None,
        rng: random.Random,
    ) -> tuple[CombatSimState, float, float]:
        weights = self.config.weights
        cooldowns = dict(state.cooldowns)
        effects = dict(state.effects)

        if action_id == WAIT_ACTION_ID:
            pending = [r for r in cooldowns.values() if r > 0.0]
            elapsed = min(min(pending), self.config.max_wait_s) if pending else self.config.min_step_s
            damage_ratio = 0.0
            heal_ratio = 0.0
            mp_cost = 0.0
            resolve_at = elapsed
            spec = None
        else:
            spec = self.book[action_id]
            elapsed = max(spec.occupancy_s, self.config.min_step_s)
            damage_ratio = spec.damage_ratio
            heal_ratio = spec.heal_ratio
            mp_cost = spec.mp_ratio_cost
            resolve_at = min(spec.cast_s, elapsed)

        taken = self._integrate_incoming(effects, state.incoming_dps, elapsed)
        dealt = damage_ratio * self._scale_at(effects, resolve_at, incoming=False)

        own_hp = min(1.0, max(0.0, state.own_hp - taken + heal_ratio))
        own_mp = min(1.0, max(0.0, state.own_mp - mp_cost))
        target_hp = min(1.0, max(0.0, state.target_hp - dealt))

        effects = {k: v - elapsed for k, v in effects.items() if v - elapsed > 0.0}
        cooldowns = {k: v - elapsed for k, v in cooldowns.items() if v - elapsed > 0.0}
        if spec is not None:
            if spec.cooldown_s > 0.0:
                cooldowns[action_id] = spec.cooldown_s
            if spec.effect is not None:
                # Thompson draw, then the Bernoulli trial it parameterises. An
                # effect whose rate is merely unmeasured still lands sometimes,
                # so the search keeps probing it; one measured as resisted stops
                # being drawn favourably without ever being hard-excluded.
                p = self.matrix.posterior(action_id, target_class).sample(rng)
                if rng.random() < p:
                    effects[spec.effect.effect_id] = max(
                        effects.get(spec.effect.effect_id, 0.0), spec.effect.duration_s
                    )

        reward = weights.damage * (state.target_hp - target_hp) - weights.risk * (
            state.own_hp - own_hp
        )
        if target_hp <= 0.0 < state.target_hp:
            reward += weights.kill_bonus
        if own_hp <= 0.0 < state.own_hp:
            reward -= weights.death_penalty

        return (
            CombatSimState(
                t=state.t + elapsed,
                own_hp=own_hp,
                own_mp=own_mp,
                target_hp=target_hp,
                incoming_dps=state.incoming_dps,
                cooldowns=_as_pairs(cooldowns),
                effects=_as_pairs(effects),
            ),
            reward,
            elapsed,
        )


@dataclass(frozen=True)
class ActionValue:
    action_id: str
    visits: int
    mean_value: float


@dataclass(frozen=True)
class SearchResult:
    action_id: str | None
    expected_value: float
    visits: int
    iterations: int
    depth: int
    seed: int
    per_action: tuple[ActionValue, ...]
    reason: str


class _Node:
    __slots__ = ("children", "visits", "total_value")

    def __init__(self) -> None:
        self.children: dict[str, _Node] = {}
        self.visits: int = 0
        self.total_value: float = 0.0

    @property
    def mean_value(self) -> float:
        return self.total_value / self.visits if self.visits else 0.0


class MonteCarloCombatSearch:
    r"""Open-loop MCTS to a fixed depth, selecting with UCT.

    Selection maximises

    .. math:: \mathrm{UCT}(s,a) = \tilde{Q}(s,a) + c\sqrt{\frac{\ln N(s)}{N(s,a)}}

    where :math:`\tilde{Q}` is :math:`Q` rescaled into :math:`[0,1]` by the
    minimum and maximum return seen so far. Without that rescaling, :math:`c`
    would silently change meaning whenever the reward weights were retuned, and
    the search would drift between greedy and near-random for reasons no test
    would attribute to the weights.

    The search is *open loop*: an edge re-simulates its stochastic transition on
    every visit rather than caching one sampled successor. Effect application is
    a coin flip, and a closed-loop tree would freeze whichever side of that flip
    it happened to expand first, then plan against a world where the stun always
    lands.
    """

    def __init__(
        self,
        book: ActionBook,
        matrix: StochasticTransitionMatrix,
        config: SearchConfig | None = None,
    ) -> None:
        self.config = config or SearchConfig()
        self.simulator = CombatSimulator(book, matrix, self.config)

    def search(
        self,
        root_state: CombatSimState,
        target_class: str | None = None,
        seed: int = 0,
        restrict_to: tuple[str, ...] | None = None,
    ) -> SearchResult:
        """Evaluate the action tree from ``root_state`` and return the best root action.

        ``restrict_to`` is how Tier A reaches the search: actions whose readiness
        is UNKNOWN are withheld here rather than being scored and then discarded,
        so the iteration budget is spent only on lines the engine could actually
        commit to.
        """
        config = self.config
        rng = random.Random(seed)
        root = _Node()

        root_legal = self._root_actions(root_state, restrict_to)
        if not root_legal:
            return SearchResult(
                action_id=None,
                expected_value=0.0,
                visits=0,
                iterations=0,
                depth=config.max_depth,
                seed=seed,
                per_action=(),
                reason="no action is both ready and affordable",
            )

        value_min = math.inf
        value_max = -math.inf

        for _ in range(config.iterations):
            node = root
            state = root_state
            path = [root]
            steps: list[tuple[float, float]] = []
            depth = 0
            expanded = False

            while depth < config.max_depth and not state.is_terminal and not expanded:
                legal = self._root_actions(state, restrict_to) if depth == 0 else \
                    self.simulator.legal_actions(state)
                if not legal:
                    break
                untried = [a for a in legal if a not in node.children]
                if untried:
                    action = untried[rng.randrange(len(untried))]
                    expanded = True
                else:
                    action = self._uct_select(node, legal, value_min, value_max)

                state, reward, elapsed = self.simulator.step(state, action, target_class, rng)
                steps.append((reward, elapsed))
                child = node.children.get(action)
                if child is None:
                    child = _Node()
                    node.children[action] = child
                node = child
                path.append(node)
                depth += 1

            while depth < config.max_depth and not state.is_terminal:
                legal = self.simulator.legal_actions(state)
                if not legal:
                    break
                action = legal[rng.randrange(len(legal))]
                state, reward, elapsed = self.simulator.step(state, action, target_class, rng)
                steps.append((reward, elapsed))
                depth += 1

            # Suffix returns: ``returns[i]`` is the discounted value of the state
            # reached after ``i`` steps. Backing the whole root return up every
            # node instead would let a weak action inherit the value of a strong
            # prefix.
            returns = [0.0] * (len(steps) + 1)
            for index in range(len(steps) - 1, -1, -1):
                reward, elapsed = steps[index]
                returns[index] = reward + (config.discount_per_second ** elapsed) * returns[index + 1]

            for index, visited in enumerate(path):
                # A child stores the *edge* value Q(s,a) = r + gamma^tau V(s'),
                # which is ``returns`` one step earlier - not V(s') itself.
                # Crediting a child with V(s') alone drops the reward of the very
                # transition that reached it, and the omission is worst exactly
                # where it matters: an action that kills the character ends the
                # episode, so V(s') is zero and the fatal line scores better than
                # every survivable one.
                value = returns[0] if index == 0 else returns[index - 1]
                visited.visits += 1
                visited.total_value += value
                value_min = min(value_min, value)
                value_max = max(value_max, value)

        per_action = tuple(
            ActionValue(action_id, child.visits, child.mean_value)
            for action_id, child in sorted(root.children.items())
        )
        if not per_action:
            return SearchResult(
                action_id=None,
                expected_value=0.0,
                visits=root.visits,
                iterations=config.iterations,
                depth=config.max_depth,
                seed=seed,
                per_action=(),
                reason="root expanded no action",
            )

        # Robust child: most-visited, not highest-mean. Under a finite budget a
        # mean over few samples is the noisier statistic, and it is the one an
        # unlucky rollout can spike.
        best = max(per_action, key=lambda item: (item.visits, item.mean_value, item.action_id))
        if best.action_id == WAIT_ACTION_ID:
            return SearchResult(
                action_id=None,
                expected_value=best.mean_value,
                visits=best.visits,
                iterations=config.iterations,
                depth=config.max_depth,
                seed=seed,
                per_action=per_action,
                reason="waiting out a cooldown scored above every available action",
            )
        return SearchResult(
            action_id=best.action_id,
            expected_value=best.mean_value,
            visits=best.visits,
            iterations=config.iterations,
            depth=config.max_depth,
            seed=seed,
            per_action=per_action,
            reason=f"depth-{config.max_depth} search over {len(per_action)} root action(s)",
        )

    def _root_actions(
        self, state: CombatSimState, restrict_to: tuple[str, ...] | None
    ) -> tuple[str, ...]:
        legal = self.simulator.legal_actions(state)
        if restrict_to is None:
            return legal
        allowed = set(restrict_to) | {WAIT_ACTION_ID}
        filtered = tuple(a for a in legal if a in allowed)
        return filtered if filtered else (WAIT_ACTION_ID,)

    def _uct_select(
        self,
        node: _Node,
        legal: tuple[str, ...],
        value_min: float,
        value_max: float,
    ) -> str:
        span = value_max - value_min
        log_parent = math.log(max(1.0, float(node.visits)))
        best_action = legal[0]
        best_score = -math.inf
        for action in legal:
            child = node.children.get(action)
            if child is None or child.visits == 0:
                return action
            normalised = 0.5 if span <= 0.0 else (child.mean_value - value_min) / span
            score = normalised + self.config.exploration_c * math.sqrt(log_parent / child.visits)
            if score > best_score:
                best_score, best_action = score, action
        return best_action
