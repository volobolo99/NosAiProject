"""Contracts for the NOS AI Agent Runtime Platform.

LLM providers are decision-only components. They never receive execution
privileges; execution remains behind Guard AI and the Safety Gate.
"""
from dataclasses import dataclass, field
from enum import Enum
from typing import Mapping, Protocol, Sequence

from nosai.core.contracts import Decision, Goal, WorldState


class PrivacyClass(str, Enum):
    PUBLIC = "PUBLIC"
    INTERNAL = "INTERNAL"
    SENSITIVE = "SENSITIVE"
    LOCAL_ONLY = "LOCAL_ONLY"


class ExecutionMode(str, Enum):
    ASSIST = "ASSIST"
    SUPERVISED = "SUPERVISED"
    AUTONOMOUS = "AUTONOMOUS"


@dataclass(frozen=True)
class RuntimeContext:
    session_id: str
    privacy: PrivacyClass = PrivacyClass.LOCAL_ONLY
    latency_budget_ms: int = 1000
    complexity: float = 0.5
    requires_local: bool = True
    metadata: Mapping[str, object] = field(default_factory=dict)


@dataclass(frozen=True)
class ResourceSnapshot:
    cpu_percent: float = 0.0
    gpu_percent: float = 0.0
    ram_available_mb: int = 0
    vram_available_mb: int = 0
    temperature_c: float = 0.0
    power_w: float = 0.0


@dataclass(frozen=True)
class ProviderCapabilities:
    provider_id: str
    local: bool
    tool_calling: bool = False
    vision: bool = False
    max_context_tokens: int = 0


class DecisionProvider(Protocol):
    """A model/provider may propose a decision, but cannot execute it."""

    @property
    def capabilities(self) -> ProviderCapabilities: ...

    def decide(self, world_state: WorldState, goal: Goal) -> Decision: ...


@dataclass(frozen=True)
class ProviderCandidate:
    provider: DecisionProvider
    score: float
    reason: str


@dataclass(frozen=True)
class PlanStep:
    name: str
    action: str
    requires_trust_tier: int = 1
    reversible: bool = True


@dataclass(frozen=True)
class AgentPlan:
    goal: Goal
    steps: Sequence[PlanStep]


@dataclass(frozen=True)
class VerificationResult:
    passed: bool
    reason: str


@dataclass(frozen=True)
class Permission:
    resource: str
    max_trust_tier: int = 1
    allow: bool = True
