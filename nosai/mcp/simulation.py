from __future__ import annotations

import hashlib
from typing import Any

from .contracts import SimulationRequest


def run_simulation(request: SimulationRequest) -> dict[str, Any]:
    """Deterministic, side-effect-free short-horizon evaluator."""
    digest = hashlib.sha256(f"{request.scenario_id}:{request.seed}".encode()).digest()
    baseline = 0.5 + (digest[0] / 2550)
    action_bonus = sum(0.01 for action in request.actions if action.get("kind"))
    score = min(1.0, baseline + action_bonus)
    return {
        "schema_version": "mcp.simulation.result.v1",
        "scenario_id": request.scenario_id,
        "seed": request.seed,
        "horizon_ticks": request.horizon_ticks,
        "score": round(score, 6),
        "completed": score >= 0.5,
        "side_effects": [],
    }


