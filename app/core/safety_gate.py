from app.core.contracts import Decision, SafetyResult, WorldState, ActionType


class SafetyGateChecker:
    """Pure validation boundary; it never executes game input."""

    def evaluate(self, decision: Decision, world_state: WorldState) -> SafetyResult:
        if decision.confidence < 0.0 or decision.confidence > 1.0:
            return SafetyResult(False, "invalid confidence")
        if decision.action in {ActionType.ATTACK, ActionType.USE_SKILL} and decision.target_id is None:
            return SafetyResult(False, "combat action requires a target")
        if world_state.hp_ratio <= 0.0 and decision.action is not ActionType.IDLE:
            return SafetyResult(False, "dead state permits only IDLE")
        return SafetyResult(True, "allowed")
