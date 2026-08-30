"""Dependency-free local HTTP server for the NosAi operator dashboard.

The server intentionally exposes only a local control surface. Runtime integration is
provided through a small adapter boundary so the UI never invents live telemetry.
"""
from __future__ import annotations

import json
import mimetypes
import os
import threading
from dataclasses import asdict, dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urlparse
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parent / "static"
HOST = os.getenv("NOSAI_DASHBOARD_HOST", "127.0.0.1")
PORT = int(os.getenv("NOSAI_DASHBOARD_PORT", "8765"))
RUNTIME_URL = os.getenv("NOSAI_RUNTIME_URL", "").rstrip("/")


@dataclass
class DashboardState:
    mode: str = "NORMAL"
    trust_level: int = 2
    observation_version: int = 0
    provider: str = "not-connected"
    connected: bool = False
    telemetry_source: str = "unavailable"
    commands: list[dict[str, Any]] = field(default_factory=list)
    config: dict[str, Any] = field(default_factory=lambda: {
        "perception_enabled": True,
        "simulation_enabled": True,
        "auto_execute": False,
        "cloud_escalation": False,
        "trace_retention": "session",
        "eye_ai_view": True,
    })


STATE = DashboardState()
LOCK = threading.RLock()


# The runtime stamps this on every snapshot. Consuming a payload without
# checking it defeats the point of versioning the contract: an incompatible
# future shape would be rendered as though it were understood.
GATE1_CONTRACT_VERSION = "gate1.snapshot.v1"


def fetch_gate1_snapshot() -> tuple[dict[str, Any] | None, str | None]:
    """Return the runtime snapshot, or None plus the reason it is unusable."""
    if not RUNTIME_URL:
        return None, "runtime_url_not_configured"
    try:
        with urllib.request.urlopen(f"{RUNTIME_URL}/api/gate1", timeout=1.5) as response:
            payload = json.loads(response.read().decode())
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, OSError, ValueError):
        return None, "runtime_unreachable"

    if not isinstance(payload, dict):
        return None, "malformed_snapshot"

    version = payload.get("contractVersion")
    if version != GATE1_CONTRACT_VERSION:
        # An unrecognised version is not a live reading. Fail closed rather than
        # showing fields whose meaning is no longer guaranteed.
        return None, "unsupported_contract_version:" + str(version or "missing")

    return payload, None


def runtime_snapshot() -> dict[str, Any]:
    """Return a truthful snapshot; no fake hardware/runtime values are generated."""
    gate1, failure = fetch_gate1_snapshot()
    with LOCK:
        payload = asdict(STATE)
    if gate1 is None:
        payload["connected"] = False
        payload["provider"] = "not-connected"
        payload["telemetry_source"] = "UNKNOWN"
        payload["gate1"] = None
        payload["gate1_failure"] = failure
        return payload
    payload["connected"] = True
    payload["provider"] = "gate1-runtime"
    payload["telemetry_source"] = "LIVE"
    payload["mode"] = str(gate1.get("runtimeStatus") or payload["mode"])
    payload["gate1"] = gate1
    payload["gate1_failure"] = None
    return payload


def enqueue_command(action: str, payload: dict[str, Any] | None = None) -> dict[str, Any]:
    command = {"action": action, "payload": payload or {}, "status": "queued"}
    with LOCK:
        STATE.commands.append(command)
        STATE.commands = STATE.commands[-100:]
    return command


class Handler(BaseHTTPRequestHandler):
    server_version = "NosAiDashboard/0.1"

    def _send(self, status: int, body: bytes, content_type: str) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _json(self, status: int, value: Any) -> None:
        self._send(status, json.dumps(value, ensure_ascii=False).encode(), "application/json; charset=utf-8")

    def do_GET(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        if path == "/api/state":
            self._json(200, runtime_snapshot())
            return
        if path == "/api/health":
            snapshot = runtime_snapshot()
            self._json(200, {
                "ok": True,
                "service": "local-dashboard",
                "runtime_connected": snapshot["connected"],
                "telemetry_source": snapshot["telemetry_source"],
            })
            return
        self._serve_static(path)

    def do_POST(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        length = int(self.headers.get("Content-Length", "0"))
        try:
            body = json.loads(self.rfile.read(length) or b"{}")
        except json.JSONDecodeError:
            self._json(400, {"error": "invalid_json"})
            return

        if path == "/api/command":
            action = body.get("action")
            allowed = {"pause", "resume", "stop", "recovery", "cooling", "checkpoint", "reobserve"}
            if action not in allowed:
                self._json(400, {"error": "unsupported_command"})
                return
            self._json(202, enqueue_command(action, body.get("payload")))
            return

        if path == "/api/config":
            with LOCK:
                for key in STATE.config:
                    if key in body:
                        STATE.config[key] = body[key]
            self._json(200, runtime_snapshot())
            return

        self._json(404, {"error": "not_found"})

    def _serve_static(self, path: str) -> None:
        relative = "index.html" if path in ("", "/") else path.lstrip("/")
        target = (ROOT / relative).resolve()
        if ROOT not in target.parents and target != ROOT:
            self._json(403, {"error": "forbidden"})
            return
        if not target.is_file():
            self._json(404, {"error": "not_found"})
            return
        content_type = mimetypes.guess_type(target.name)[0] or "application/octet-stream"
        self._send(200, target.read_bytes(), content_type)

    def log_message(self, fmt: str, *args: Any) -> None:
        print(f"[dashboard] {self.address_string()} - {fmt % args}")


def serve(host: str = HOST, port: int = PORT) -> None:
    httpd = ThreadingHTTPServer((host, port), Handler)
    print(f"NosAi local dashboard: http://{host}:{port}")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()


if __name__ == "__main__":
    serve()
