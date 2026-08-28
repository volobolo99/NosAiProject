from app.core.contracts import ActionType, Decision, DecisionProvider, Goal, WorldState


class RuleBasedDecisionProvider:
    def decide(self, world_state: WorldState, goal: Goal) -> Decision:
        if world_state.hp_ratio < 0.30:
            return Decision(ActionType.RETREAT, world_state.target_id, 0.90, "low HP")
        if world_state.target_id is not None:
            return Decision(ActionType.ATTACK, world_state.target_id, 0.80, "target available")
        return Decision(ActionType.IDLE, None, 0.70, "no target")
