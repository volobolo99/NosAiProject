"""Deterministic decision provider used as baseline and fallback."""

from nosai.core.contracts import ActionType, CandidateAction, Decision, DecisionProvider, Goal, WorldState


class RuleBasedDecisionProvider:
    """Conservative deterministic provider.

    It never requires an LLM and therefore provides a stable baseline for
    simulation, CI and runtime fallback.
    """

    def decide(self, world_state: WorldState, goal: Goal) -> Decision:
        del goal
        if world_state.hp <= 20:
            action = CandidateAction(ActionType.RECOVER)
            return Decision(action=action, confidence=1.0, reasoning="HP is critically low")
        if world_state.target_id is not None and (world_state.target_hp is None or world_state.target_hp > 0):
            action = CandidateAction(ActionType.ATTACK, target_id=world_state.target_id)
            return Decision(action=action, confidence=0.8, reasoning="Observed valid target")
        return Decision(
            action=CandidateAction(ActionType.NOOP),
            confidence=1.0,
            reasoning="No actionable target observed",
        )
