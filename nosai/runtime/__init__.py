"""NOS AI Agent Runtime Platform integration layer.

Version remains 1.0 Beta. Runtime providers are decision-only and all
execution remains downstream of Guard AI and Safety Gate.
"""
from .contracts import AgentPlan, DecisionProvider, ExecutionMode, Permission, PrivacyClass, ProviderCapabilities, ResourceSnapshot, RuntimeContext, VerificationResult
from .engine import AgentRuntime, RuntimeDecision
from .memory import MemoryBus, MemoryEvent
from .policy import ExecutionPolicy, RoutingPolicy
from .provider_router import ProviderRegistry, ProviderRouter
from .resources import HardwareCapabilities, ResourceManager
from .session import AgentSession, SessionManager
from .trust import TrustBoundary, TrustPolicy, TrustTier
from .agent_loop import AgentLoop, LoopResult
from .tools import ToolRegistry, ToolSpec
from .hardware import HardwareProfiler, HardwareSnapshot, RuntimeProfile
from .session_protocol import MessageType, SessionMessage, SequenceGuard
from .evaluation import AgentTrace, EvaluationRecorder, EvaluationScore

__all__ = [
    "AgentRuntime", "RuntimeDecision", "AgentPlan", "DecisionProvider", "ExecutionMode",
    "HardwareCapabilities", "Permission", "PrivacyClass", "ProviderCapabilities",
    "ResourceSnapshot", "RuntimeContext", "VerificationResult", "MemoryBus", "MemoryEvent",
    "ExecutionPolicy", "RoutingPolicy", "ProviderRegistry", "ProviderRouter", "ResourceManager",
    "AgentSession", "SessionManager", "TrustBoundary", "TrustPolicy", "TrustTier", "AgentLoop",
    "LoopResult", "ToolRegistry", "ToolSpec", "HardwareProfiler", "HardwareSnapshot", "RuntimeProfile",
    "MessageType", "SessionMessage", "SequenceGuard", "AgentTrace", "EvaluationRecorder", "EvaluationScore",
]
