"""Deterministic trust tiers for runtime actions."""
from dataclasses import dataclass
from enum import IntEnum

class TrustTier(IntEnum):
    OBSERVE = 0
    SIMULATE = 1
    REVERSIBLE = 2
    SENSITIVE = 3
    CRITICAL = 4

@dataclass(frozen=True)
class TrustPolicy:
    max_tier: TrustTier = TrustTier.SIMULATE
    require_guard: bool = True
    require_safety: bool = True

    def allows(self, tier: TrustTier) -> bool:
        return tier <= self.max_tier

class TrustBoundary:
    """Fail-closed authorization independent from any model output."""
    def __init__(self, policy: TrustPolicy | None = None):
        self.policy = policy or TrustPolicy()

    def authorize(self, tier: TrustTier, guard_allowed: bool = False, safety_allowed: bool = False) -> bool:
        if not self.policy.allows(tier):
            return False
        if self.policy.require_guard and not guard_allowed:
            return False
        if self.policy.require_safety and not safety_allowed:
            return False
        return True
