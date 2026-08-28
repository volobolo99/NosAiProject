"""Simulation-first Planner -> Guard -> Executor -> Verifier loop."""
from dataclasses import dataclass
from typing import Callable, Protocol
from .trust import TrustTier, TrustBoundary
from .contracts import AgentPlan, VerificationResult

class Planner(Protocol):
    def plan(self, context: object) -> AgentPlan: ...

class Executor(Protocol):
    def execute(self, action: object) -> object: ...

class Verifier(Protocol):
    def verify(self, expected: object, observed: object) -> VerificationResult: ...

@dataclass(frozen=True)
class LoopResult:
    plan: AgentPlan
    verification: VerificationResult
    executed: bool

class AgentLoop:
    def __init__(self, planner: Planner, executor: Executor, verifier: Verifier,
                 guard: Callable[[object], bool], safety: Callable[[object], bool],
                 trust: TrustBoundary | None = None):
        self.planner = planner
        self.executor = executor
        self.verifier = verifier
        self.guard = guard
        self.safety = safety
        self.trust = trust or TrustBoundary()

    def run(self, context: object, tier: TrustTier = TrustTier.SIMULATE) -> LoopResult:
        plan = self.planner.plan(context)
        if not plan.steps:
            return LoopResult(plan, VerificationResult(False, "empty plan"), False)
        step = plan.steps[0]
        guard_ok = self.guard(step)
        safety_ok = self.safety(step)
        if not self.trust.authorize(tier, guard_ok, safety_ok):
            return LoopResult(plan, VerificationResult(False, "trust/guard/safety boundary rejected action"), False)
        observed = self.executor.execute(step)
        verification = self.verifier.verify(step, observed)
        return LoopResult(plan, verification, True)
