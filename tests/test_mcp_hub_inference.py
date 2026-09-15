import pytest

from nosai.mcp.enforcement import RoleEnforcementError
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


def test_infer_blocks_forbidden_capability_before_routing():
    """Il gate deve scattare prima di router.choose(): un router senza provider
    solleverebbe comunque un RuntimeError, quindi solo RoleEnforcementError
    (e non un RuntimeError di routing) dimostra che il blocco avviene prima."""
    router = ModelRouter.from_config({"providers": []}, McpPolicy())
    gateway = InferenceGateway(router, None)
    with pytest.raises(RoleEnforcementError):
        gateway.infer("hello", capability="secret_export", role_id="employee.security")


def test_infer_without_role_stays_permissive_up_to_routing():
    """Chiamate senza role_id restano permissive rispetto all'enforcement:
    l'errore atteso e' quello di routing (nessun provider), non un blocco di ruolo."""
    router = ModelRouter.from_config({"providers": []}, McpPolicy())
    gateway = InferenceGateway(router, None)
    with pytest.raises(RuntimeError) as excinfo:
        gateway.infer("hello")
    assert not isinstance(excinfo.value, RoleEnforcementError)


