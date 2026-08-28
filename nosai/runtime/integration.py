"""Bridge the deterministic NOS AI decision pipeline into the autonomous runtime."""
from dataclasses import dataclass
from typing import Callable

from nosai.core.contracts import ActionType, Decision, Goal, WorldState
from nosai.core.tactical_ranking import RankedAction
from .agent_loop import AgentLoop, LoopResult
from .contracts import AgentPlan, PlanStep
from .trust import TrustTier


@dataclass(frozen=True)
class RuntimeIntegrationConfig:
    default_tier: TrustTier = TrustTier.SIMULATE
    max_steps: int = 8


def plan_from_ranked_actions(goal: Goal, ranked: tuple[RankedAction, ...], max_steps: int = 8) -> AgentPlan:
    """Convert deterministic tactical candidates into a bounded runtime plan."""
    steps: list[PlanStep] = []
    for item in ranked[:max_steps]:
        action = item.action
        steps.append(PlanStep(
            name=f"{action.actor_id or 'player'}:{action.action.value}",
            action=action.action.value,
            requires_trust_tier=int(TrustTier.SIMULATE),
            reversible=action.action in {ActionType.NOOP, ActionType.MOVE, ActionType.RECOVER, ActionType.PICKUP},
        ))
    return AgentPlan(goal=goal, steps=tuple(steps))


class RankedActionPlanner:
    """Planner adapter: tactical ranking remains deterministic and model-independent."""
    def __init__(self, ranked_actions: Callable[[object], tuple[RankedAction, ...]], config: RuntimeIntegrationConfig | None = None):
        self.ranked_actions = ranked_actions
        self.config = config or RuntimeIntegrationConfig()

    def plan(self, context: object) -> AgentPlan:
        if not isinstance(context, dict) or not isinstance(context.get("goal"), Goal):
            raise ValueError("runtime context must contain a Goal")
        ranked = tuple(self.ranked_actions(context))
        return plan_from_ranked_actions(context["goal"], ranked, self.config.max_steps)


class FixedDecisionPlanner:
    """Turns an existing domain decision into one bounded runtime step."""
    def __init__(self, decision: Decision, goal: Goal, tier: TrustTier):
        self.decision, self.goal, self.tier = decision, goal, tier

    def plan(self, _context: object) -> AgentPlan:
        action = self.decision.action
        return AgentPlan(goal=self.goal, steps=(PlanStep(
            name=f"decision:{action.action.value}",
            action=action.action.value,
            requires_trust_tier=int(self.tier),
            reversible=action.action in {ActionType.NOOP, ActionType.MOVE, ActionType.RECOVER, ActionType.PICKUP},
        ),))


class NosAiRuntimeBridge:
    """Connect an existing NOS AI decision to AgentLoop without model execution rights."""
    def __init__(self, loop: AgentLoop, config: RuntimeIntegrationConfig | None = None):
        self.loop = loop
        self.config = config or RuntimeIntegrationConfig()

    def execute_decision(self, world_state: WorldState, goal: Goal, decision: Decision) -> LoopResult:
        original = self.loop.planner
        self.loop.planner = FixedDecisionPlanner(decision, goal, self.config.default_tier)
        try:
            return self.loop.run({"world_state": world_state, "goal": goal, "decision": decision}, self.config.default_tier)
        finally:
            self.loop.planner = original
