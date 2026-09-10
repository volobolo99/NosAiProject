from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .audit import AuditLog
from .config import load_config
from .contracts import ActivationRequest, SimulationRequest, ToolRisk
from .learning import LearningFactory
from .policy import McpPolicy
from .router import ModelRouter
from .simulation import run_simulation

try:
    from mcp.server.fastmcp import FastMCP
except ImportError:  # pragma: no cover - allows contract tests without MCP installed
    FastMCP = None  # type: ignore[assignment,misc]


def create_server(config_path: Path | str | None = None):
    if FastMCP is None:
        raise RuntimeError("MCP runtime dependency is missing; install the project's mcp extra")
    config = load_config(config_path)
    policy = McpPolicy()
    if config.get("network_enabled"):
        # Configuration cannot silently enable network mode; only the operator action can.
        config["network_enabled"] = False
    router = ModelRouter.from_config(config, policy)
    audit = AuditLog(config["audit_path"])
    learning = LearningFactory(config["learning_path"])
    server = FastMCP("nosai-mcp-hub")

    @server.tool()
    def mcp_status() -> str:
        return json.dumps({"schema_version": "mcp.status.v1", "mode": policy.mode.value, "network_enabled": policy.network_enabled, "providers": router.catalog()}, ensure_ascii=False)

    @server.tool()
    def mcp_activate_network(request_json: str) -> str:
        request = ActivationRequest.from_mapping(json.loads(request_json))
        policy.apply(request)
        audit.append("network_mode_changed", {"enabled": policy.network_enabled, "mode": policy.mode.value})
        return mcp_status()

    @server.tool()
    def mcp_provider_catalog() -> str:
        return json.dumps({"schema_version": "mcp.provider.catalog.v1", "providers": router.catalog()}, ensure_ascii=False)

    @server.tool()
    def mcp_choose_provider(capability: str = "") -> str:
        decision = router.choose(capability or None)
        audit.append("provider_selected", {"provider_id": decision.provider_id, "model_id": decision.model_id, "network_used": decision.network_used})
        return json.dumps(decision.__dict__, ensure_ascii=False)

    @server.tool()
    def mcp_run_simulation(request_json: str) -> str:
        request = SimulationRequest.from_mapping(json.loads(request_json))
        result = run_simulation(request)
        audit.append("simulation_completed", {"scenario_id": request.scenario_id, "seed": request.seed})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_learning_candidate(topic: str, payload_json: str, source: str, evidence_count: int) -> str:
        candidate = learning.record_candidate(topic, json.loads(payload_json), source, evidence_count)
        return json.dumps({"candidate_id": candidate.candidate_id, "status": candidate.status}, ensure_ascii=False)

    @server.tool()
    def mcp_validate_learning(candidate_id: str) -> str:
        result = learning.validate_candidate(candidate_id)
        return json.dumps({"candidate_id": result.candidate_id, "status": result.status.value, "reason": result.reason}, ensure_ascii=False)

    @server.resource("nosai://mcp/policy")
    def mcp_policy_resource() -> str:
        return json.dumps({"network_default": False, "privileged_tools": "denied", "secret_export": "denied", "execution_authority": "local-safety-gate"}, ensure_ascii=False)

    return server


def run(config_path: Path | str | None = None) -> None:
    create_server(config_path).run(transport="stdio")


