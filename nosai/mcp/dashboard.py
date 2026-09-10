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
from .policy import McpPolicy, PolicyViolation
from .router import ModelRouter
from .secrets import SecretStore


class McpDashboardService:
    def __init__(self, config_path: Path | str | None = None):
        config = load_config(config_path)
        self.policy = McpPolicy()
        self.router = ModelRouter.from_config(config, self.policy)
        self.audit = AuditLog(config["audit_path"])
        self._secret_path = config["secret_path"]

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
            if path == "/api/mcp/providers":
                self._json(200, {"providers": service.router.catalog()})
                return
            if path == "/api/mcp/secrets":
                try:
                    self._json(200, {"secrets": service.secret_metadata()})
                except RuntimeError as exc:
                    self._json(503, {"error": str(exc)})
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


