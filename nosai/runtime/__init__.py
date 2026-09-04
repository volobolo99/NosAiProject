"""NOS AI Agent Runtime Platform integration layer.

Version remains 1.0 Beta. Runtime providers are decision-only; execution is
coordinated by the runtime control plane and its configured policy gates.
"""
from .contracts import AgentPlan, DecisionProvider, ExecutionMode, Permission, PrivacyClass, ProviderCapabilities, ResourceSnapshot, RuntimeContext, VerificationResult
from .engine import AgentRuntime, RuntimeDecision
from .memory import MemoryBus, MemoryEvent
from .policy import ExecutionPolicy, RoutingPolicy
from .provider_router import ProviderRegistry, ProviderRouter
from .resources import HardwareCapabilities, ResourceManager
from .session import AgentSession, SessionManager
from .trust import TrustBoundary, TrustPolicy, TrustTier
from .agent_loop import AgentLoop, LoopResult, RecoveryPolicy, StepTrace
from .recovery import RecoveryController, RecoveryEvent
from .watchdog import RuntimeWatchdog, WatchdogMode, WatchdogPolicy
from .tools import ToolRegistry, ToolSpec
from .hardware import HardwareProfiler, HardwareSnapshot, RuntimeProfile, InferenceBudget, recommended_profile_for_nitro_v16
from .hardware_watchdog import HardwareTelemetry, HardwareProbe, PsutilNvmlProbe, WatchdogDecision, NOSAIHardwareWatchdog
from .context_slimming import ExceptionSignature, VRAMContextSlimmer
from .adaptive_throttling import AdaptiveLimits, AdaptiveThrottler, ResourcePlan, ThrottleMode
from .session_protocol import MessageType, SessionMessage, SequenceGuard
from .evaluation import AgentTrace, EvaluationRecorder, EvaluationScore
from .integration import NosAiRuntimeBridge, RuntimeIntegrationConfig, RankedActionPlanner, plan_from_ranked_actions
from .orchestrator_bridge import OrchestratorRuntimePlanner, run_orchestrated_tick
from .closed_loop import ClosedLoopRuntime, ClosedLoopResult, ClosedLoopStep
from .events import EventBus, RuntimeEvent
from .state import WorldStateStore, VersionedWorldState

__all__ = [
    "AgentRuntime", "RuntimeDecision", "AgentPlan", "DecisionProvider", "ExecutionMode",
    "HardwareCapabilities", "Permission", "PrivacyClass", "ProviderCapabilities", "ResourceSnapshot", "RuntimeContext", "VerificationResult",
    "MemoryBus", "MemoryEvent", "ExecutionPolicy", "RoutingPolicy", "ProviderRegistry", "ProviderRouter", "ResourceManager",
    "AgentSession", "SessionManager", "TrustBoundary", "TrustPolicy", "TrustTier", "AgentLoop", "LoopResult", "RecoveryPolicy", "StepTrace",
    "RecoveryController", "RecoveryEvent", "RuntimeWatchdog", "WatchdogMode", "WatchdogPolicy", "ToolRegistry", "ToolSpec", "HardwareProfiler", "HardwareSnapshot", "RuntimeProfile", "InferenceBudget", "recommended_profile_for_nitro_v16",
    "HardwareTelemetry", "HardwareProbe", "PsutilNvmlProbe", "WatchdogDecision", "NOSAIHardwareWatchdog", "ExceptionSignature", "VRAMContextSlimmer",
    "AdaptiveLimits", "AdaptiveThrottler", "ResourcePlan", "ThrottleMode",
    "MessageType", "SessionMessage", "SequenceGuard", "AgentTrace", "EvaluationRecorder", "EvaluationScore", "NosAiRuntimeBridge", "RuntimeIntegrationConfig",
    "RankedActionPlanner", "plan_from_ranked_actions", "OrchestratorRuntimePlanner", "run_orchestrated_tick", "ClosedLoopRuntime", "ClosedLoopResult", "ClosedLoopStep",
    "EventBus", "RuntimeEvent", "WorldStateStore", "VersionedWorldState",
]
