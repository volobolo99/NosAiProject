"""Deterministic integration bridge from NOS AI domain decisions to Agent Runtime.

The bridge is simulation-first: it converts a Decision into a bounded AgentPlan;
execution remains behind Guard/Safety and the runtime never grants a DecisionProvider
execution privileges.
"""
from dataclasses import dataclass
from typing import Callable

from nosai.core.contracts import Decision, Goal, WorldState
from .agent_loop import AgentLoop, LoopResult
from .contracts import AgentPlan, PlanStep
from .trust import TrustTier


@dataclass(frozen=True)
class RuntimeIntegrationConfig:
    default_tier: TrustTier = TrustTier.SIMULATE
    max_steps: int = 8


class NosAiRuntimeBridge:
    """Connects the existing decision pipeline to the autonomous runtime."""

    def __init__(self, loop: AgentLoop, config: RuntimeIntegrationConfig | None = None):
        self.loop = loop
        self.config = config or RuntimeIntegrationConfig()

    def execute_decision(self, world_state: WorldState, goal: Goal, decision: Decision) -> LoopResult:
        """Wrap one deterministic domain decision as a runtime plan.

        The decision is data only. Guard, Safety, Trust and Executor remain the
        only path to execution.
        """
        step = PlanStep(
            name=decision.action.action.value,
            action=decision.action.action.value,
            requires_trust_tier=int(self.config.default_tier),
            reversible=decision.action.action.value not in {"ATTACK", "SKILL"},
        )
        plan = AgentPlan(goal=goal, steps=(step,))

        class FixedPlanner:
            def plan(self, _context: object) -> AgentPlan:
                return plan

        # Reuse the configured loop's safety boundary while injecting the
        # already-produced domain decision as the runtime plan.
        original = self.loop.planner
        self.loop.planner = FixedPlanner()
        try:
            return self.loop.run({"world_state": world_state, "decision": decision}, self.config.default_tier)
        finally:
            self.loop.planner = original
