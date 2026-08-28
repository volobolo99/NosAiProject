"""Adapter between NosAiOrchestrator tick results and AgentLoop.

Core orchestration remains deterministic. This adapter only translates ranked
actions into a bounded runtime plan; Guard/Safety remain authoritative.
"""
from __future__ import annotations

from typing import Any

from nosai.core.contracts import Goal
from nosai.core.orchestrator import TickResult
from .agent_loop import AgentLoop, LoopResult
from .contracts import AgentPlan, PlanStep
from .integration import plan_from_ranked_actions
from .trust import TrustTier


class OrchestratorRuntimePlanner:
    """Planner that consumes a previously computed orchestrator tick."""

    def __init__(self, config_max_steps: int = 8):
        self.config_max_steps = config_max_steps

    def plan(self, context: object) -> AgentPlan:
        if not isinstance(context, dict):
            raise ValueError("runtime context must be a mapping")
        goal = context.get("goal")
        tick = context.get("tick")
        if not isinstance(goal, Goal) or not isinstance(tick, TickResult):
            raise ValueError("runtime context requires Goal and TickResult")
        if not tick.safety_allowed:
            return AgentPlan(goal=goal, steps=())
        return plan_from_ranked_actions(goal, tick.ranked_actions, self.config_max_steps)


def run_orchestrated_tick(loop: AgentLoop, world_state: Any, goal: Goal, tick: TickResult) -> LoopResult:
    """Run a completed deterministic orchestration tick through the autonomous runtime."""
    original = loop.planner
    loop.planner = OrchestratorRuntimePlanner()
    try:
        return loop.run({"world_state": world_state, "goal": goal, "tick": tick}, TrustTier.SIMULATE)
    finally:
        loop.planner = original
