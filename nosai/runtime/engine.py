"""Safety-gated agent runtime facade.

The engine can plan and verify, but only the downstream execution adapter may
perform actions after Guard AI and Safety Gate approval.
"""
from dataclasses import dataclass

from nosai.core.contracts import Decision, Goal, WorldState
from nosai.core.safety import SafetyGate
from nosai.guard.runtime import GuardAI

from .contracts import RuntimeContext, VerificationResult
from .memory import MemoryBus, MemoryEvent
from .provider_router import ProviderRouter
from .resources import ResourceManager
from .session import AgentSession, SessionManager


@dataclass(frozen=True)
class RuntimeDecision:
    decision: Decision
    provider_id: str
    guard_allowed: bool
    safety_allowed: bool
    reason: str


class AgentRuntime:
    def __init__(
        self,
        provider_router: ProviderRouter,
        guard: GuardAI | None = None,
        safety_gate: SafetyGate | None = None,
        resources: ResourceManager | None = None,
        sessions: SessionManager | None = None,
        memory: MemoryBus | None = None,
    ) -> None:
        self.provider_router = provider_router
        self.guard = guard or GuardAI()
        self.safety_gate = safety_gate or SafetyGate()
        self.resources = resources or ResourceManager()
        self.sessions = sessions or SessionManager()
        self.memory = memory or MemoryBus()

    def decide(self, world: WorldState, goal: Goal, context: RuntimeContext) -> RuntimeDecision:
        session = self.sessions.get(context.session_id)
        provider_candidate = self.provider_router.select(context, self.resources.snapshot())
        decision = provider_candidate.provider.decide(world, goal)
        guard = self.guard.evaluate(world, decision.action)
        if not guard.allowed:
            result = RuntimeDecision(decision, provider_candidate.provider.capabilities.provider_id, False, False, guard.reason)
            self._record(session, result)
            return result
        safety = self.safety_gate.evaluate(decision.action, world)
        result = RuntimeDecision(decision, provider_candidate.provider.capabilities.provider_id, True, safety.allowed, safety.reason)
        self._record(session, result)
        return result

    def verify(self, session_id: str, result: object, expected_success: bool = True) -> VerificationResult:
        passed = bool(result) == expected_success
        reason = "verification_passed" if passed else "verification_failed"
        self.memory.publish(MemoryEvent(session_id, "VERIFICATION", {"passed": passed, "reason": reason}))
        return VerificationResult(passed, reason)

    def _record(self, session: AgentSession, result: RuntimeDecision) -> None:
        self.memory.publish(MemoryEvent(session.session_id, "DECISION", {
            "provider_id": result.provider_id,
            "guard_allowed": result.guard_allowed,
            "safety_allowed": result.safety_allowed,
            "reason": result.reason,
            "action": result.decision.action.action.value,
        }))
