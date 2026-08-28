"""Offline tactical simulator for NosAi.

This module deliberately models game decisions without connecting to a live client.
It is the foundation for Guard AI / Progression Engine experimentation: state,
actions, stochastic outcomes, utility scoring, rollout evaluation and reusable
strategy traces are all explicit and testable.
"""
from __future__ import annotations

from dataclasses import dataclass, field, replace
from enum import Enum
import math
import random
from typing import Iterable, Sequence


class Element(str, Enum):
    NONE = "none"
    FIRE = "fire"
    SHADOW = "shadow"
    WATER = "water"
    LIGHT = "light"


@dataclass(frozen=True)
class Combatant:
    id: str
    hp: int
    max_hp: int
    mp: int
    max_mp: int
    attack: int
    defense: int
    element: Element = Element.NONE
    resistance: dict[Element, int] = field(default_factory=dict)
    alive: bool = True


@dataclass(frozen=True)
class TacticalState:
    tick: int
    time_left: float
    player: Combatant
    enemies: tuple[Combatant, ...]
    potions: int = 0
    objective_progress: float = 0.0
    objective_target: float = 1.0
    deaths: int = 0
    score: float = 0.0
    history: tuple[str, ...] = ()

    @property
    def terminal(self) -> bool:
        return self.time_left <= 0 or not self.player.alive or self.objective_progress >= self.objective_target or not any(e.alive for e in self.enemies)

    @property
    def success(self) -> bool:
        return self.objective_progress >= self.objective_target or not any(e.alive for e in self.enemies)


@dataclass(frozen=True)
class TacticalAction:
    name: str
    target_id: str | None = None
    expected_damage: int = 0
    mana_cost: int = 0
    duration: float = 0.5
    success_probability: float = 1.0
    progress_delta: float = 0.0
    resource_cost: float = 0.0


@dataclass(frozen=True)
class SimulationConfig:
    damage_variance: float = 0.12
    enemy_counter_damage: int = 6
    action_failure_damage: int = 0
    hp_weight: float = 0.30
    progress_weight: float = 2.0
    time_weight: float = 0.45
    resource_weight: float = 0.20
    death_penalty: float = 8.0


@dataclass(frozen=True)
class ActionEvaluation:
    action: TacticalAction
    mean_utility: float
    success_rate: float
    mean_time: float
    mean_damage_taken: float
    mean_resource_cost: float


@dataclass(frozen=True)
class PlanResult:
    actions: tuple[TacticalAction, ...]
    utility: float
    success_probability: float
    expected_time: float
    expected_resource_cost: float
    explored_nodes: int


class TacticalSimulator:
    """State-transition model. No network, client or input APIs are used."""

    _ADVANTAGE = {
        Element.FIRE: Element.SHADOW,
        Element.SHADOW: Element.WATER,
        Element.WATER: Element.LIGHT,
        Element.LIGHT: Element.FIRE,
    }

    def __init__(self, config: SimulationConfig | None = None) -> None:
        self.config = config or SimulationConfig()

    def legal_actions(self, state: TacticalState) -> tuple[TacticalAction, ...]:
        if state.terminal:
            return ()
        actions: list[TacticalAction] = []
        for enemy in state.enemies:
            if not enemy.alive:
                continue
            advantage = self._ADVANTAGE.get(state.player.element) == enemy.element
            multiplier = 1.5 if advantage else 1.0
            resistance = enemy.resistance.get(state.player.element, 0)
            multiplier *= max(0.0, 1.0 - resistance / 100.0)
            damage = max(1, int((state.player.attack - enemy.defense) * multiplier))
            actions.append(TacticalAction(
                name="attack",
                target_id=enemy.id,
                expected_damage=damage,
                duration=0.45,
                success_probability=0.99,
                resource_cost=0.0,
            ))
        if state.potions > 0 and state.player.hp < state.player.max_hp * 0.70:
            actions.append(TacticalAction(name="heal", expected_damage=0, duration=0.35, resource_cost=1.0))
        if state.objective_progress < state.objective_target:
            actions.append(TacticalAction(name="advance", duration=0.25, progress_delta=0.10, success_probability=0.98))
        return tuple(actions)

    def step(self, state: TacticalState, action: TacticalAction, rng: random.Random) -> TacticalState:
        if state.terminal:
            return state
        player = state.player
        enemies = list(state.enemies)
        damage_taken = 0
        progress = state.objective_progress
        potions = state.potions
        score_delta = 0.0
        succeeded = rng.random() <= action.success_probability

        if action.name == "attack" and succeeded and action.target_id:
            idx = next(i for i, e in enumerate(enemies) if e.id == action.target_id)
            enemy = enemies[idx]
            variance = 1.0 + rng.uniform(-self.config.damage_variance, self.config.damage_variance)
            dealt = max(1, int(action.expected_damage * variance))
            new_hp = max(0, enemy.hp - dealt)
            enemies[idx] = replace(enemy, hp=new_hp, alive=new_hp > 0)
            score_delta += dealt * 0.08
        elif action.name == "heal" and succeeded and potions > 0:
            heal = min(35, player.max_hp - player.hp)
            player = replace(player, hp=player.hp + heal)
            potions -= 1
        elif action.name == "advance" and succeeded:
            progress = min(state.objective_target, progress + action.progress_delta)

        # Surviving enemies get one bounded counter-action per simulated tick.
        alive_enemies = [e for e in enemies if e.alive]
        if alive_enemies and player.alive:
            damage_taken = sum(max(0, self.config.enemy_counter_damage + e.attack // 20 - player.defense // 20) for e in alive_enemies[:1])
            player = replace(player, hp=max(0, player.hp - damage_taken), alive=player.hp - damage_taken > 0)

        deaths = state.deaths + (0 if player.alive else 1)
        time_left = max(0.0, state.time_left - action.duration)
        score_delta += progress * self.config.progress_weight
        score_delta -= action.duration * self.config.time_weight
        score_delta -= action.resource_cost * self.config.resource_weight
        score_delta -= self.config.death_penalty if not player.alive else 0.0

        return TacticalState(
            tick=state.tick + 1,
            time_left=time_left,
            player=player,
            enemies=tuple(enemies),
            potions=potions,
            objective_progress=progress,
            objective_target=state.objective_target,
            deaths=deaths,
            score=state.score + score_delta,
            history=state.history + (action.name + (f":{action.target_id}" if action.target_id else ""),),
        )

    def rollout(self, state: TacticalState, actions: Sequence[TacticalAction], seed: int = 0) -> TacticalState:
        rng = random.Random(seed)
        current = state
        for action in actions:
            current = self.step(current, action, rng)
            if current.terminal:
                break
        return current

    def evaluate_action(self, state: TacticalState, action: TacticalAction, rollouts: int = 32, seed: int = 0) -> ActionEvaluation:
        results = [self.rollout(state, (action,), seed + i) for i in range(rollouts)]
        success_rate = sum(r.success for r in results) / len(results)
        mean_utility = sum(self.utility(r) for r in results) / len(results)
        mean_time = sum(state.time_left - r.time_left for r in results) / len(results)
        mean_damage = sum(max(0, state.player.hp - r.player.hp) for r in results) / len(results)
        mean_resources = sum(state.potions - r.potions for r in results) / len(results)
        return ActionEvaluation(action, mean_utility, success_rate, mean_time, mean_damage, mean_resources)

    def utility(self, state: TacticalState) -> float:
        hp_ratio = state.player.hp / max(1, state.player.max_hp)
        progress_ratio = state.objective_progress / max(1e-9, state.objective_target)
        time_used = 1.0 - state.time_left / max(1.0, state.time_left + 1.0)
        return (
            progress_ratio * self.config.progress_weight
            + hp_ratio * self.config.hp_weight
            + state.score
            - time_used * self.config.time_weight
            - state.deaths * self.config.death_penalty
        )


class BeamSearchPlanner:
    """Risk-aware bounded planner; suitable for deterministic CI and later Guard AI."""

    def __init__(self, simulator: TacticalSimulator, width: int = 12, depth: int = 8, rollouts: int = 12) -> None:
        self.sim = simulator
        self.width = width
        self.depth = depth
        self.rollouts = rollouts

    def plan(self, state: TacticalState, seed: int = 0) -> PlanResult:
        frontier: list[tuple[TacticalState, tuple[TacticalAction, ...]]] = [(state, ())]
        explored = 0
        for depth in range(self.depth):
            candidates: list[tuple[float, TacticalState, tuple[TacticalAction, ...]]] = []
            for current, path in frontier:
                for action in self.sim.legal_actions(current):
                    explored += 1
                    evaluation = self.sim.evaluate_action(current, action, self.rollouts, seed + explored)
                    # Penalize uncertainty: Guard AI prefers high-confidence branches.
                    risk_adjusted = evaluation.mean_utility + 1.5 * evaluation.success_rate - 0.15 * evaluation.mean_damage_taken
                    next_state = self.sim.rollout(current, (action,), seed + explored)
                    candidates.append((risk_adjusted, next_state, path + (action,)))
            candidates.sort(key=lambda x: x[0], reverse=True)
            frontier = [(s, p) for _, s, p in candidates[: self.width]]
            if not frontier or all(s.terminal for s, _ in frontier):
                break

        best_state, best_path = max(frontier, key=lambda item: self.sim.utility(item[0]), default=(state, ()))
        success_probability = self._estimate_plan_success(state, best_path, seed + 100_000)
        return PlanResult(
            actions=best_path,
            utility=self.sim.utility(best_state),
            success_probability=success_probability,
            expected_time=sum(a.duration for a in best_path),
            expected_resource_cost=sum(a.resource_cost for a in best_path),
            explored_nodes=explored,
        )

    def _estimate_plan_success(self, state: TacticalState, path: Sequence[TacticalAction], seed: int) -> float:
        if not path:
            return 0.0
        successes = 0
        for i in range(self.rollouts * 4):
            result = self.sim.rollout(state, path, seed + i)
            successes += int(result.success)
        return successes / (self.rollouts * 4)
