import json

from nosai.mcp.contracts import (
    ActivationRequest,
    McpMode,
    SimulationRequest,
)


def test_activation_request_defaults_network_off():
    request = ActivationRequest.from_mapping({"enabled": True})
    assert request.enabled is True
    assert request.mode is McpMode.NETWORK
    assert request.confirmation == "operator"


def test_simulation_request_round_trips_without_unknown_fields():
    request = SimulationRequest.from_mapping(
        {
            "scenario_id": "combat/basic",
            "seed": 7,
            "horizon_ticks": 20,
            "actions": [{"kind": "move", "target": "north"}],
        }
    )
    encoded = request.to_json()
    decoded = json.loads(encoded)
    assert decoded["scenario_id"] == "combat/basic"
    assert decoded["seed"] == 7
    assert decoded["horizon_ticks"] == 20
    assert decoded["actions"][0]["kind"] == "move"


