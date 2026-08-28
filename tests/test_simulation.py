from nosai.core.contracts import ActionType, CandidateAction, Goal, WorldState
from nosai.core.simulation import DeterministicSimulator


def test_simulation_is_deterministic_and_fail_closed():
    state = WorldState(hp=100, mp=50, target_id=7, tick_id=1)
    goal = Goal("combat")
    proposed = CandidateAction(ActionType.ATTACK, target_id=99)

    simulator = DeterministicSimulator()
    first = simulator.tick(state, goal, proposed)
    second = simulator.tick(state, goal, proposed)

    assert first == second
    assert first.executed.action is ActionType.NOOP
