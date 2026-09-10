from __future__ import annotations

import json
import os
from dataclasses import asdict
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from .audit import AuditLog
from .config import load_config
from .contracts import ActivationRequest
from .contracts import SimulationRequest
from .simulation import run_simulation
from .director import McpDirector, ChangeProposal
from .auditor import McpAuditor
from .chief import McpChief
from .policy import McpPolicy, PolicyViolation
from .router import ModelRouter
from .secrets import SecretStore
from .roles import DEFAULT_EMPLOYEE_ROLES, RoleArchitect


class McpDashboardService:
    def __init__(self, config_path: Path | str | None = None):
        config = load_config(config_path)
        self.policy = McpPolicy()
        self.router = ModelRouter.from_config(config, self.policy)
        self._chief = McpChief(Path(config.get("role_bindings_path", "data/mcp/role_bindings.json")).parent, bindings=self.router.role_bindings)
        self.audit = AuditLog(config["audit_path"])
        self._secret_path = config["secret_path"]
        self._director = McpDirector("data/mcp/proposals")
        self._auditor = McpAuditor()

    def role_bindings(self) -> list[dict[str, Any]]:
        registry = self.router.role_bindings
        return registry.list() if registry is not None else []

    def role_proposals(self) -> list[dict[str, Any]]:
        registry = self.router.role_bindings
        return registry.proposals() if registry is not None else []

    def propose_role_binding(self, body: dict[str, Any]) -> dict[str, Any]:
        registry = self.router.role_bindings
        if registry is None:
            raise RuntimeError("role binding registry is not configured")
        proposal = self._chief.bindings.propose(
            str(body.get("employee_id", "")),
            str(body.get("primary_model", "")),
            list(body.get("fallback_models", [])),
        )
        self.audit.append("dashboard_role_binding_proposed", {"proposal_id": proposal["proposal_id"], "employee_id": proposal["employee_id"]})
        return proposal

    def promote_role_binding(self, body: dict[str, Any]) -> dict[str, Any]:
        registry = self.router.role_bindings
        if registry is None:
            raise RuntimeError("role binding registry is not configured")
        result = self._chief.promote_binding(
            str(body.get("proposal_id", "")),
            dict(body.get("checks", {})),
            str(body.get("confirmation", "")),
        )
        self.audit.append("dashboard_role_binding_promoted", {"proposal_id": result.get("proposal_id"), "employee_id": result["employee_id"], "version": result["version"]})
        return result

    def rollback_role_binding(self, body: dict[str, Any]) -> dict[str, Any]:
        registry = self.router.role_bindings
        if registry is None:
            raise RuntimeError("role binding registry is not configured")
        result = self._chief.rollback_binding(str(body.get("employee_id", "")), str(body.get("confirmation", "")))
        self.audit.append("dashboard_role_binding_rolled_back", {"employee_id": result["employee_id"], "version": result["version"]})
        return result

    def employee_roles(self) -> list[dict[str, Any]]:
        return [
            {
                "employee_id": role.employee_id,
                "display_name": role.display_name,
                "purpose": role.purpose,
                "responsibilities": list(role.responsibilities),
                "primary_model": role.primary_model,
                "fallback_models": list(role.fallback_models),
                "capabilities": list(role.capabilities),
                "forbidden": list(role.forbidden),
                "approval_required": role.approval_required,
            }
            for role in DEFAULT_EMPLOYEE_ROLES
        ]

    def verify_employee_roles(self) -> dict[str, Any]:
        errors = RoleArchitect.verify_employee_catalog(DEFAULT_EMPLOYEE_ROLES)
        return {
            "valid": not errors,
            "errors": errors,
            "count": len(DEFAULT_EMPLOYEE_ROLES),
        }

    def chief_health(self) -> dict[str, Any]:
        """Run a read-only health tick for the MCP Chief watchdog."""
        result = self._chief.health_check()
        self.audit.append("dashboard_chief_health_checked", {"status": result["status"], "failed_checks": result["failed_checks"]})
        return result

    def status(self) -> dict[str, Any]:
        return {
            "schema_version": "mcp.dashboard.status.v1",
            "mode": self.policy.mode.value,
            "network_enabled": self.policy.network_enabled,
            "network_default": False,
            "providers": self.router.catalog(),
            "secret_values_exposed": False,
        }

    def activate(self, body: dict[str, Any]) -> dict[str, Any]:
        self.policy.apply(ActivationRequest.from_mapping(body))
        self.audit.append("dashboard_policy_changed", {"network_enabled": self.policy.network_enabled})
        return self.status()

    def secret_metadata(self) -> list[dict[str, Any]]:
        store = SecretStore(self._secret_path)
        return [asdict(item) for item in store.metadata()]

    def upsert_secret(self, provider_id: str, value: str) -> dict[str, Any]:
        store = SecretStore(self._secret_path)
        result = store.upsert(provider_id, value)
        self.audit.append("secret_metadata_updated", {"provider_id": provider_id, "fingerprint": result.fingerprint})
        return asdict(result)

    def delete_secret(self, provider_id: str) -> dict[str, Any]:
        store = SecretStore(self._secret_path)
        return {"provider_id": provider_id, "deleted": store.delete(provider_id)}

    def simulate(self, body: dict[str, Any]) -> dict[str, Any]:
        result = run_simulation(SimulationRequest.from_mapping(body))
        self.audit.append("dashboard_simulation", {"scenario_id": result["scenario_id"], "seed": result["seed"]})
        return result

    def propose_change(self, body: dict[str, Any]) -> dict[str, Any]:
        proposal = self._director.propose(str(body.get("component", "")), str(body.get("summary", "")), list(body.get("files", [])))
        return {"proposal_id": proposal.proposal_id, "component": proposal.component, "summary": proposal.summary, "files": list(proposal.files), "status": proposal.status}

    def audit_change(self, body: dict[str, Any]) -> dict[str, Any]:
        proposal = ChangeProposal(**body["proposal"])
        verdict = self._auditor.review(proposal, dict(body.get("checks", {})))
        return asdict(verdict)


def make_handler(service: McpDashboardService):
    static_root = Path(__file__).resolve().parent / "static"

    class Handler(BaseHTTPRequestHandler):
        server_version = "NosAiMcpDashboard/1.0"

        def _json(self, status: int, value: Any) -> None:
            payload = json.dumps(value, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def do_GET(self) -> None:  # noqa: N802
            path = urlparse(self.path).path
            if path == "/api/mcp/status":
                self._json(200, service.status())
                return
            if path == "/api/mcp/chief":
                self._json(200, service.chief_health())
                return
            if path == "/api/mcp/providers":
                self._json(200, {"providers": service.router.catalog()})
                return
            if path == "/api/mcp/roles":
                self._json(200, {"roles": service.employee_roles()})
                return
            if path == "/api/mcp/roles/verify":
                self._json(200, service.verify_employee_roles())
                return
            if path == "/api/mcp/role-bindings":
                self._json(200, {"bindings": service.role_bindings()})
                return
            if path == "/api/mcp/role-proposals":
                self._json(200, {"proposals": service.role_proposals()})
                return
            if path == "/api/mcp/secrets":
                try:
                    self._json(200, {"secrets": service.secret_metadata()})
                except RuntimeError as exc:
                    self._json(503, {"error": str(exc)})
                return
            if path == "/api/mcp/audit":
                if not self._audit_path().is_file():
                    self._json(200, {"events": []})
                else:
                    lines = self._audit_path().read_text(encoding="utf-8").splitlines()[-100:]
                    self._json(200, {"events": [json.loads(line) for line in lines]})
                return
            target = static_root / ("index.html" if path in ("", "/") else path.lstrip("/"))
            if static_root not in target.resolve().parents or not target.is_file():
                self._json(404, {"error": "not_found"})
                return
            payload = target.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def do_POST(self) -> None:  # noqa: N802
            length = int(self.headers.get("Content-Length", "0"))
            try:
                body = json.loads(self.rfile.read(length) or b"{}")
            except json.JSONDecodeError:
                self._json(400, {"error": "invalid_json"})
                return
            path = urlparse(self.path).path
            if path == "/api/mcp/activate":
                try:
                    self._json(200, service.activate(body))
                except (PolicyViolation, ValueError) as exc:
                    self._json(403, {"error": str(exc)})
                return
            if path == "/api/mcp/simulate":
                try:
                    self._json(200, service.simulate(body))
                except (ValueError, TypeError) as exc:
                    self._json(400, {"error": str(exc)})
                return
            if path == "/api/mcp/proposals":
                try:
                    self._json(200, service.propose_change(body))
                except (PermissionError, ValueError) as exc:
                    self._json(403, {"error": str(exc)})
                return
            if path == "/api/mcp/role-bindings/propose":
                try:
                    self._json(200, service.propose_role_binding(body))
                except (KeyError, PermissionError, ValueError, TypeError) as exc:
                    self._json(400, {"error": str(exc)})
                return
            if path == "/api/mcp/role-bindings/promote":
                try:
                    self._json(200, service.promote_role_binding(body))
                except (KeyError, PermissionError, ValueError, TypeError) as exc:
                    self._json(403, {"error": str(exc)})
                return
            if path == "/api/mcp/role-bindings/rollback":
                try:
                    self._json(200, service.rollback_role_binding(body))
                except (KeyError, PermissionError, ValueError, TypeError) as exc:
                    self._json(403, {"error": str(exc)})
                return
            if path == "/api/mcp/audit-change":
                try:
                    self._json(200, service.audit_change(body))
                except (KeyError, TypeError, ValueError) as exc:
                    self._json(400, {"error": str(exc)})
                return
            if path == "/api/mcp/secrets":
                try:
                    self._json(200, service.upsert_secret(str(body.get("provider_id", "")), str(body.get("value", ""))))
                except (RuntimeError, ValueError) as exc:
                    self._json(400, {"error": str(exc)})
                return
            self._json(404, {"error": "not_found"})

        def do_DELETE(self) -> None:  # noqa: N802
            path = urlparse(self.path)
            if path.path != "/api/mcp/secrets":
                self._json(404, {"error": "not_found"})
                return
            provider_id = path.query.removeprefix("provider_id=")
            try:
                self._json(200, service.delete_secret(provider_id))
            except RuntimeError as exc:
                self._json(503, {"error": str(exc)})

        def _audit_path(self) -> Path:
            return service.audit.path

        def log_message(self, fmt: str, *args: Any) -> None:
            print(f"[mcp-dashboard] {self.address_string()} - {fmt % args}")

    return Handler


def serve(host: str | None = None, port: int | None = None, config_path: Path | str | None = None) -> int:
    address = host or os.getenv("NOSAI_MCP_DASHBOARD_HOST", "127.0.0.1")
    selected_port = port or int(os.getenv("NOSAI_MCP_DASHBOARD_PORT", "8770"))
    server = ThreadingHTTPServer((address, selected_port), make_handler(McpDashboardService(config_path)))
    print(f"NosAi MCP dashboard: http://{address}:{selected_port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0

