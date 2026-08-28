"""Traceable agent evaluation primitives for offline regression tests."""
from dataclasses import dataclass, field
from time import monotonic

@dataclass(frozen=True)
class AgentTrace:
    task_id: str
    events: tuple[str, ...] = ()
    latency_ms: float = 0.0
    safety_blocks: int = 0
    tool_calls: int = 0

@dataclass
class EvaluationRecorder:
    task_id: str
    _events: list[str] = field(default_factory=list)
    _started: float = field(default_factory=monotonic)
    _blocks: int = 0
    _tools: int = 0

    def event(self, name: str) -> None: self._events.append(name)
    def safety_block(self) -> None: self._blocks += 1; self._events.append("SAFETY_BLOCK")
    def tool_call(self) -> None: self._tools += 1; self._events.append("TOOL_CALL")
    def snapshot(self) -> AgentTrace:
        return AgentTrace(self.task_id, tuple(self._events), (monotonic()-self._started)*1000, self._blocks, self._tools)

class EvaluationScore:
    @staticmethod
    def score(trace: AgentTrace, success: bool) -> float:
        if not success: return 0.0
        penalty = min(0.5, trace.safety_blocks * 0.1 + max(0, trace.tool_calls - 10) * 0.01)
        return max(0.0, 1.0 - penalty)
