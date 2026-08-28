from nosai.runtime import (
    AgentLoop, AgentPlan, HardwareProfiler, HardwareSnapshot, LoopResult,
    MessageType, SequenceGuard, SessionMessage, TrustBoundary, TrustPolicy,
    TrustTier, VerificationResult,
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


def test_agent_loop_stops_at_safety_boundary():
    class P:
        def plan(self, _): return AgentPlan(Goal("test"), (type("S", (), {"name":"x","action":"x","requires_trust_tier":1,"reversible":True})(),))
    class E:
        def execute(self, _): raise AssertionError("executor must not run")
    class V:
        def verify(self, *_): return VerificationResult(True, "ok")
    loop = AgentLoop(P(), E(), V(), guard=lambda _: False, safety=lambda _: True)
    result = loop.run(object())
    assert isinstance(result, LoopResult)
    assert not result.executed
