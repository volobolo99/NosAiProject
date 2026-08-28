from nosai.runtime import (
    AgentLoop, AgentPlan, HardwareProfiler, HardwareSnapshot, LoopResult,
    MessageType, SequenceGuard, SessionMessage, TrustBoundary, TrustPolicy,
    TrustTier, VerificationResult, RecoveryPolicy, RuntimeWatchdog, WatchdogPolicy,
)
from nosai.core.contracts import Goal


def test_trust_boundary_is_fail_closed():
    boundary = TrustBoundary(TrustPolicy(max_tier=TrustTier.REVERSIBLE))
    assert not boundary.authorize(TrustTier.REVERSIBLE)
    assert boundary.authorize(TrustTier.REVERSIBLE, True, True)
    assert not boundary.authorize(TrustTier.SENSITIVE, True, True)


def test_hardware_profiles_are_deterministic():
    profiler = HardwareProfiler()
    assert profiler.profile(HardwareSnapshot(vram_mb=24000, ram_mb=32000)).name == "high"
    assert profiler.profile(HardwareSnapshot(vram_mb=8000, ram_mb=16000)).name == "balanced"
    assert profiler.profile(HardwareSnapshot()).name == "constrained"


def test_session_sequence_guard_rejects_replay():
    guard = SequenceGuard()
    msg = SessionMessage("s1", 1, MessageType.HELLO, {})
    assert guard.accept(msg)
    assert not guard.accept(msg)
    assert guard.accept(SessionMessage("s1", 2, MessageType.HEARTBEAT, {}))


def _step(name: str, tier: int = 1):
    return type("S", (), {"name": name, "action": name, "requires_trust_tier": tier, "reversible": True})()


def test_agent_loop_executes_and_verifies_all_steps():
    class P:
        def plan(self, _): return AgentPlan(Goal("test"), (_step("one"), _step("two")))
    class E:
        def __init__(self): self.calls = []
        def execute(self, action): self.calls.append(action.action); return True
    class V:
        def verify(self, *_): return VerificationResult(True, "ok")
    executor = E()
    loop = AgentLoop(P(), executor, V(), guard=lambda _: True, safety=lambda _: True,
                     trust=TrustBoundary(TrustPolicy(max_tier=TrustTier.REVERSIBLE)))
    result = loop.run(object(), TrustTier.REVERSIBLE)
    assert result.completed and result.executed
    assert executor.calls == ["one", "two"]
    assert [t.status for t in result.traces] == ["VERIFIED", "VERIFIED"]


def test_agent_loop_replans_after_verification_failure():
    class P:
        def __init__(self): self.calls = 0
        def plan(self, context):
            self.calls += 1
            name = "repair" if isinstance(context, dict) and "recovery_reason" in context else "bad"
            return AgentPlan(Goal("test"), (_step(name),))
    class E:
        def execute(self, action): return action.action
    class V:
        def verify(self, expected, observed): return VerificationResult(observed == "repair", "not repaired")
    planner = P()
    loop = AgentLoop(planner, E(), V(), guard=lambda _: True, safety=lambda _: True,
                     trust=TrustBoundary(TrustPolicy(max_tier=TrustTier.SIMULATE)),
                     recovery=RecoveryPolicy(max_replans=1, max_retries_per_step=0))
    result = loop.run({}, TrustTier.SIMULATE)
    assert result.completed and result.replans == 1
    assert planner.calls == 2


def test_agent_loop_watchdog_stops_before_executor():
    class P:
        def plan(self, _): return AgentPlan(Goal("test"), (_step("one"),))
    class E:
        def execute(self, _): raise AssertionError("watchdog must stop execution")
    class V:
        def verify(self, *_): return VerificationResult(True, "ok")
    watchdog = RuntimeWatchdog(WatchdogPolicy(max_actions=0))
    loop = AgentLoop(P(), E(), V(), guard=lambda _: True, safety=lambda _: True, watchdog=watchdog)
    result = loop.run(object())
    assert not result.executed
    assert result.recovery_reason == "action_budget_exhausted"


def test_agent_loop_stops_at_safety_boundary():
    class P:
        def plan(self, _): return AgentPlan(Goal("test"), (_step("x"),))
    class E:
        def execute(self, _): raise AssertionError("executor must not run")
    class V:
        def verify(self, *_): return VerificationResult(True, "ok")
    loop = AgentLoop(P(), E(), V(), guard=lambda _: False, safety=lambda _: True)
    result = loop.run(object())
    assert isinstance(result, LoopResult)
    assert not result.executed
