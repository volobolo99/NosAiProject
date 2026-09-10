from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .audit import AuditLog
from .config import load_config
from .contracts import ActivationRequest, SimulationRequest, ToolRisk
from .learning import LearningFactory
from .chief import McpChief
from .roles import DEFAULT_EMPLOYEE_ROLES, RoleArchitect
from .policy import McpPolicy
from .router import ModelRouter
from .inference import InferenceGateway
from .secrets import SecretStore
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
    chief = McpChief(Path(config.get("role_bindings_path", "data/mcp/role_bindings.json")).parent, bindings=router.role_bindings, router=router)
    audit = AuditLog(config["audit_path"])
    learning = LearningFactory(config["learning_path"])
    try:
        secret_store = SecretStore(config["secret_path"])
    except RuntimeError:
        # Offline MCP remains usable without provider credentials. Network inference
        # fails closed until the operator configures the encrypted store.
        secret_store = None
    inference = InferenceGateway(router, secret_store)
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
    def mcp_infer(prompt: str, capability: str = "", role_id: str = "") -> str:
        result = inference.infer(prompt, capability or None, role_id=role_id or None)
        audit.append("inference_completed", {"capability": capability, "role_id": role_id or None, "provider": router.choose(capability or None, role_id or None).provider_id})
        return json.dumps(result, ensure_ascii=False)

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

    @server.tool()
    def mcp_propose_change(component: str, summary: str, files_json: str) -> str:
        proposal = chief.propose_improvement(component, summary, json.loads(files_json))
        audit.append("change_proposed", {"proposal_id": proposal["proposal_id"], "component": component, "files": list(proposal["files"])})
        return json.dumps({"proposal_id": proposal["proposal_id"], "status": proposal["status"], "files": list(proposal["files"])}, ensure_ascii=False)

    @server.tool()
    def mcp_audit_change(proposal_json: str, checks_json: str) -> str:
        proposal = json.loads(proposal_json)
        verdict = chief.audit_proposal(proposal, json.loads(checks_json))
        audit.append("change_audited", {"proposal_id": verdict["proposal_id"], "approved": verdict["approved"], "rollback_required": verdict["rollback_required"]})
        return json.dumps(verdict, ensure_ascii=False)

    @server.tool()
    def mcp_role_catalog() -> str:
        bindings = router.role_bindings.list() if router.role_bindings is not None else []
        proposals = router.role_bindings.proposals() if router.role_bindings is not None else []
        return json.dumps({"schema_version": "mcp.role.catalog.v1", "bindings": bindings, "proposals": proposals}, ensure_ascii=False)

    @server.tool()
    def mcp_role_propose_binding(employee_id: str, primary_model: str, fallback_models_json: str = "[]") -> str:
        if router.role_bindings is None:
            raise RuntimeError("role binding registry is not configured")
        proposal = chief.bindings.propose(employee_id, primary_model, json.loads(fallback_models_json))
        audit.append("role_binding_proposed", {"proposal_id": proposal["proposal_id"], "employee_id": employee_id})
        return json.dumps(proposal, ensure_ascii=False)

    @server.tool()
    def mcp_role_promote_binding(proposal_id: str, checks_json: str, confirmation: str = "") -> str:
        if router.role_bindings is None:
            raise RuntimeError("role binding registry is not configured")
        result = chief.promote_binding(proposal_id, json.loads(checks_json), confirmation)
        audit.append("role_binding_promoted", {"proposal_id": proposal_id, "employee_id": result["employee_id"], "version": result["version"]})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_role_rollback_binding(employee_id: str, confirmation: str = "") -> str:
        if router.role_bindings is None:
            raise RuntimeError("role binding registry is not configured")
        result = chief.rollback_binding(employee_id, confirmation)
        audit.append("role_binding_rolled_back", {"employee_id": employee_id, "version": result["version"]})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_chief_health() -> str:
        """Return the MCP Chief health report and immutable authority boundaries."""
        result = chief.health_check()
        audit.append("chief_health_checked", {"status": result["status"], "failed_checks": result["failed_checks"]})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_chief_recommendations() -> str:
        """Return actionable recommendations without mutating the runtime."""
        result = chief.health_check()
        return json.dumps({"schema_version": "mcp.chief.recommendations.v1", "recommendations": result["recommendations"]}, ensure_ascii=False)

    @server.tool()
    def mcp_verify_roles() -> str:
        errors = RoleArchitect().verify()
        employee_errors = RoleArchitect.verify_employee_catalog(DEFAULT_EMPLOYEE_ROLES)
        all_errors = errors + employee_errors
        return json.dumps({"valid": not all_errors, "errors": all_errors, "employee_count": len(DEFAULT_EMPLOYEE_ROLES)}, ensure_ascii=False)

    @server.resource("nosai://mcp/policy")
    def mcp_policy_resource() -> str:
        return json.dumps({"network_default": False, "privileged_tools": "denied", "secret_export": "denied", "execution_authority": "local-safety-gate"}, ensure_ascii=False)

    return server


def run(config_path: Path | str | None = None) -> None:
    create_server(config_path).run(transport="stdio")

