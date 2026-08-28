"""Fail-closed safety boundary for proposed actions."""

from .contracts import ActionType, CandidateAction, SafetyResult, WorldState


class SafetyGate:
    """Validate decisions before they can reach an executor."""

    def evaluate(self, action: CandidateAction, world_state: WorldState) -> SafetyResult:
        if action.action is ActionType.NOOP:
            return SafetyResult(True, "NOOP is always safe")

        if action.action in {ActionType.ATTACK, ActionType.SKILL, ActionType.PICKUP}:
            if action.target_id is None:
                return SafetyResult(False, "target required for target-bound action")
            if world_state.target_id != action.target_id:
                return SafetyResult(False, "action target does not match observed target")

        if not 0 <= world_state.hp <= 100:
            return SafetyResult(False, "invalid observed HP")
        if not 0 <= world_state.mp <= 100:
            return SafetyResult(False, "invalid observed MP")

        return SafetyResult(True, "action accepted")

    def enforce(self, action: CandidateAction, world_state: WorldState) -> CandidateAction:
        result = self.evaluate(action, world_state)
        return action if result.allowed else CandidateAction(action=ActionType.NOOP)
