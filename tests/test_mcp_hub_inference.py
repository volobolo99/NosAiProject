from nosai.mcp.inference import InferenceGateway
from nosai.mcp.policy import McpPolicy
from nosai.mcp.router import ModelRouter


def test_inference_cannot_call_network_provider_when_offline():
    router = ModelRouter.from_config(
        {"providers": [{"provider_id": "groq-free", "model_id": "x", "tier": 0, "network_required": True}]},
        McpPolicy(),
    )
    gateway = InferenceGateway(router, None)
    try:
        gateway.infer("hello")
    except RuntimeError as exc:
        assert "no qualified provider" in str(exc)
    else:
        raise AssertionError("offline policy must block network inference")


