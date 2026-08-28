"""Small deterministic simulation harness used before game integration."""

from dataclasses import dataclass

from .contracts import CandidateAction, Goal, WorldState
from .safety import SafetyGate


@dataclass(frozen=True)
class SimulationTick:
    tick_id: int
    world_state: WorldState
    proposed: CandidateAction
    executed: CandidateAction


class DeterministicSimulator:
    def __init__(self, safety_gate: SafetyGate | None = None) -> None:
        self.safety_gate = safety_gate or SafetyGate()

    def tick(self, world_state: WorldState, goal: Goal, proposed: CandidateAction) -> SimulationTick:
        del goal  # Goal remains part of the runtime contract for future providers.
        executed = self.safety_gate.enforce(proposed, world_state)
        return SimulationTick(world_state.tick_id, world_state, proposed, executed)
