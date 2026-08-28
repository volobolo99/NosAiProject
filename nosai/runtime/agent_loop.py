"""Autonomous, simulation-first Planner -> Guard -> Safety -> Executor -> Verifier loop.

The loop is model-agnostic and fail-closed. Plans may contain multiple steps;
each step is independently authorized. Verification failures and executor
errors are bounded recovery inputs and may trigger replanning.
"""
from dataclasses import dataclass
from time import monotonic
from typing import Any, Callable, Protocol

from .contracts import AgentPlan, VerificationResult
from .session import AgentSession
from .trust import TrustBoundary, TrustTier


class Planner(Protocol):
    def plan(self, context: object) -> AgentPlan: ...


class Executor(Protocol):
    def execute(self, action: object) -> object: ...


class Verifier(Protocol):
    def verify(self, expected: object, observed: object) -> VerificationResult: ...


@dataclass(frozen=True)
class RecoveryPolicy:
    max_steps: int = 32
    max_replans: int = 3
    max_retries_per_step: int = 1
    step_timeout_ms: int = 5000
    allow_replan_after_verification_failure: bool = True


@dataclass(frozen=True)
class StepTrace:
    index: int
    action: str
    status: str
    attempts: int = 0
    reason: str = ""
    elapsed_ms: float = 0.0


@dataclass(frozen=True)
class LoopResult:
    plan: AgentPlan
    verification: VerificationResult
    executed: bool
    completed: bool = False
    replans: int = 0
    traces: tuple[StepTrace, ...] = ()
    recovery_reason: str = ""


class AgentLoop:
    """Bounded autonomous loop with checkpointing and deterministic recovery."""

    def __init__(self, planner: Planner, executor: Executor, verifier: Verifier,
                 guard: Callable[[object], bool], safety: Callable[[object], bool],
                 trust: TrustBoundary | None = None,
                 recovery: RecoveryPolicy | None = None,
                 session: AgentSession | None = None,
                 on_recovery: Callable[[str, int], None] | None = None):
        self.planner, self.executor, self.verifier = planner, executor, verifier
        self.guard, self.safety = guard, safety
        self.trust = trust or TrustBoundary()
        self.recovery = recovery or RecoveryPolicy()
        self.session, self.on_recovery = session, on_recovery

    def run(self, context: object, tier: TrustTier = TrustTier.SIMULATE) -> LoopResult:
        replans = 0
        traces: list[StepTrace] = []
        executed_any = False
        plan = self.planner.plan(context)
        if not plan.steps:
            return LoopResult(plan, VerificationResult(False, "empty plan"), False)

        step_index = 0
        retries = 0
        while step_index < len(plan.steps) and step_index < self.recovery.max_steps:
            step = plan.steps[step_index]
            action_name = getattr(step, "action", str(step))
            started = monotonic()
            try:
                required_tier = TrustTier(step.requires_trust_tier)
            except (AttributeError, ValueError):
                required_tier = TrustTier.CRITICAL
            guard_ok = self.guard(step)
            safety_ok = self.safety(step)
            effective_tier = max(tier, required_tier)
            if not self.trust.authorize(effective_tier, guard_ok, safety_ok):
                reason = "trust/guard/safety boundary rejected action"
                traces.append(StepTrace(step_index, action_name, "BLOCKED", retries, reason, self._elapsed_ms(started)))
                self._checkpoint(step_index, "BLOCKED", reason)
                return LoopResult(plan, VerificationResult(False, reason), executed_any, False, replans, tuple(traces), reason)

            try:
                observed = self.executor.execute(step)
                executed_any = True
            except Exception as exc:
                reason = f"executor_error:{type(exc).__name__}"
                traces.append(StepTrace(step_index, action_name, "EXECUTOR_ERROR", retries + 1, reason, self._elapsed_ms(started)))
                self._checkpoint(step_index, "EXECUTOR_ERROR", reason)
                retries += 1
                if retries <= self.recovery.max_retries_per_step:
                    self._recover(reason, step_index)
                    continue
                if replans < self.recovery.max_replans:
                    replans += 1
                    self._recover(reason, step_index)
                    plan = self.planner.plan(self._repair_context(context, reason, step_index))
                    step_index, retries = 0, 0
                    if plan.steps:
                        continue
                return LoopResult(plan, VerificationResult(False, reason), executed_any, False, replans, tuple(traces), reason)

            elapsed = self._elapsed_ms(started)
            if elapsed > self.recovery.step_timeout_ms:
                reason = "step_timeout"
                traces.append(StepTrace(step_index, action_name, "TIMEOUT", retries + 1, reason, elapsed))
                self._checkpoint(step_index, "TIMEOUT", reason)
                if replans < self.recovery.max_replans:
                    replans += 1
                    self._recover(reason, step_index)
                    plan = self.planner.plan(self._repair_context(context, reason, step_index))
                    step_index, retries = 0, 0
                    continue
                return LoopResult(plan, VerificationResult(False, reason), executed_any, False, replans, tuple(traces), reason)

            verification = self.verifier.verify(step, observed)
            if verification.passed:
                traces.append(StepTrace(step_index, action_name, "VERIFIED", retries + 1, verification.reason, elapsed))
                self._checkpoint(step_index, "VERIFIED", verification.reason)
                step_index += 1
                retries = 0
                continue

            reason = verification.reason or "verification_failed"
            traces.append(StepTrace(step_index, action_name, "VERIFY_FAILED", retries + 1, reason, elapsed))
            self._checkpoint(step_index, "VERIFY_FAILED", reason)
            retries += 1
            if retries <= self.recovery.max_retries_per_step:
                self._recover(reason, step_index)
                continue
            if self.recovery.allow_replan_after_verification_failure and replans < self.recovery.max_replans:
                replans += 1
                self._recover(reason, step_index)
                plan = self.planner.plan(self._repair_context(context, reason, step_index))
                step_index, retries = 0, 0
                if plan.steps:
                    continue
            return LoopResult(plan, verification, executed_any, False, replans, tuple(traces), reason)

        if step_index >= self.recovery.max_steps:
            reason = "step_budget_exhausted"
            return LoopResult(plan, VerificationResult(False, reason), executed_any, False, replans, tuple(traces), reason)
        return LoopResult(plan, VerificationResult(True, "plan_completed"), executed_any, True, replans, tuple(traces))

    @staticmethod
    def _elapsed_ms(started: float) -> float:
        return (monotonic() - started) * 1000.0

    def _checkpoint(self, index: int, status: str, reason: str) -> None:
        if self.session is not None:
            self.session.checkpoint(step_index=index, step_status=status, reason=reason)

    def _recover(self, reason: str, step_index: int) -> None:
        if self.on_recovery is not None:
            self.on_recovery(reason, step_index)

    @staticmethod
    def _repair_context(context: object, reason: str, step_index: int) -> object:
        if isinstance(context, dict):
            repaired: dict[str, Any] = dict(context)
            repaired.update(recovery_reason=reason, failed_step_index=step_index)
            return repaired
        return {"context": context, "recovery_reason": reason, "failed_step_index": step_index}
