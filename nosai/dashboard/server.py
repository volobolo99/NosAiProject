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

ROOT = Path(__file__).resolve().parent / "static"
HOST = os.getenv("NOSAI_DASHBOARD_HOST", "127.0.0.1")
PORT = int(os.getenv("NOSAI_DASHBOARD_PORT", "8765"))


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


def runtime_snapshot() -> dict[str, Any]:
    """Return a truthful snapshot; no fake hardware/runtime values are generated."""
    with LOCK:
        return asdict(STATE)


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
            self._json(200, {"ok": True, "service": "local-dashboard", "runtime_connected": STATE.connected})
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
