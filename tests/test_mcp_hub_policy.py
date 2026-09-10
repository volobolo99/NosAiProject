from nosai.mcp.contracts import McpMode, ToolRisk
from nosai.mcp.policy import McpPolicy, PolicyViolation


def test_network_is_disabled_by_default():
    policy = McpPolicy()
    assert policy.mode is McpMode.OFFLINE
    assert policy.network_enabled is False


def test_network_activation_requires_operator_confirmation():
    policy = McpPolicy()
    try:
        policy.activate_network(confirmation="model")
    except PolicyViolation as exc:
        assert "operator" in str(exc)
    else:  # pragma: no cover - protects the invariant
        raise AssertionError("model confirmation must never activate network mode")


def test_privileged_and_secret_export_tools_are_always_denied():
    policy = McpPolicy()
    for risk in (ToolRisk.PRIVILEGED, ToolRisk.SECRET_EXPORT):
        assert policy.authorize_tool("test", risk) is False


