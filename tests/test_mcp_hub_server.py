import json

from nosai.mcp.config import load_config
from nosai.mcp.policy import McpPolicy
from nosai.mcp.router import ModelRouter


def test_config_cannot_enable_network_without_operator_action(tmp_path):
    config = tmp_path / "mcp.json"
    config.write_text(json.dumps({"network_enabled": True}), encoding="utf-8")
    loaded = load_config(config)
    policy = McpPolicy()
    router = ModelRouter.from_config(loaded, policy)
    assert policy.network_enabled is False
    assert router.choose().network_used is False


