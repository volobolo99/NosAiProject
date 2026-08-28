"""Deterministic local-first routing and permission policy."""
from dataclasses import dataclass

from .contracts import ExecutionMode, PrivacyClass, ProviderCandidate, ResourceSnapshot, RuntimeContext


@dataclass(frozen=True)
class RoutingPolicy:
    max_temperature_c: float = 85.0
    min_vram_mb: int = 512
    local_latency_margin_ms: int = 100
    allow_cloud_for_sensitive: bool = False
    max_power_w: float = 1000.0

    def allows_cloud(self, context: RuntimeContext) -> bool:
        if context.requires_local:
            return False
        if context.privacy in {PrivacyClass.SENSITIVE, PrivacyClass.LOCAL_ONLY}:
            return self.allow_cloud_for_sensitive
        return True

    def score(self, context: RuntimeContext, resources: ResourceSnapshot, local: bool) -> float:
        if local and context.privacy in {PrivacyClass.LOCAL_ONLY, PrivacyClass.SENSITIVE}:
            return 100.0
        if not local and not self.allows_cloud(context):
            return float("-inf")
        score = 50.0
        score += max(0.0, 30.0 - context.complexity * 20.0) if local else context.complexity * 20.0
        score += max(0.0, min(20.0, context.latency_budget_ms / 100.0)) if local else 5.0
        if local:
            if resources.temperature_c > self.max_temperature_c:
                return float("-inf")
            if resources.vram_available_mb < self.min_vram_mb:
                score -= 25.0
        return score


@dataclass(frozen=True)
class ExecutionPolicy:
    mode: ExecutionMode = ExecutionMode.SUPERVISED
    max_trust_tier: int = 1
    require_human_for_irreversible: bool = True

    def permits(self, trust_tier: int, reversible: bool = True) -> bool:
        if trust_tier > self.max_trust_tier:
            return False
        if self.require_human_for_irreversible and not reversible and self.mode != ExecutionMode.AUTONOMOUS:
            return False
        return True
