"""Deterministic Guard AI runtime foundation.

The guard evaluates proposed actions before execution. It never executes actions.
"""
from dataclasses import dataclass
from enum import IntEnum
from nosai.core.contracts import CandidateAction, SafetyResult, WorldState

class TrustTier(IntEnum):
    TIER_1 = 1
    TIER_2 = 2
    TIER_3 = 3
    TIER_4 = 4

@dataclass(frozen=True)
class GuardDecision:
    allowed: bool
    tier: TrustTier
    reason: str

class GuardAI:
    def __init__(self, max_tier: TrustTier = TrustTier.TIER_1):
        self.max_tier = max_tier

    def evaluate(self, world: WorldState, action: CandidateAction) -> GuardDecision:
        if world.hp <= 0:
            return GuardDecision(False, TrustTier.TIER_1, "player_not_alive")
        if action.action.value == "NOOP":
            return GuardDecision(True, TrustTier.TIER_1, "noop")
        tier = TrustTier.TIER_1
        if action.action.value in {"MOVE", "PICKUP"}:
            tier = TrustTier.TIER_1
        elif action.action.value in {"ATTACK", "SKILL", "RECOVER"}:
            tier = TrustTier.TIER_2
        if tier > self.max_tier:
            return GuardDecision(False, tier, "trust_tier_not_authorized")
        return GuardDecision(True, tier, "allowed")

    def safety_result(self, decision: GuardDecision) -> SafetyResult:
        return SafetyResult(allowed=decision.allowed, reason=decision.reason)
