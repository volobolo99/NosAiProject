"""The operator UI and the C# runtime are two processes on two ports.

These tests pin the wiring that made a healthy runtime render as "not connected":
a runtime URL that defaulted to empty, and a dashboard port shared with the
runtime's own operator API so whichever process started second failed to bind.
"""
from __future__ import annotations

import re
import socket
from pathlib import Path

from nosai.dashboard import server

REPO_ROOT = Path(__file__).resolve().parent.parent
GATE1_OPTIONS = REPO_ROOT / "src" / "NosAi.Runtime" / "Configuration" / "Gate1HostOptions.cs"


def _free_port() -> int:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def test_default_runtime_url_matches_the_runtime_operator_port():
    # Drift here is silent: the UI would poll a port nothing serves and report
    # "not connected" while the runtime is perfectly healthy.
    source = GATE1_OPTIONS.read_text(encoding="utf-8")
    match = re.search(r"DefaultDashboardPort\s*=\s*(\d+)", source)
    assert match, "Gate1HostOptions.DefaultDashboardPort not found"
    assert server.DEFAULT_RUNTIME_URL == f"http://127.0.0.1:{match.group(1)}"


def test_runtime_operator_port_differs_from_the_dashboard_port():
    assert str(server.PORT) not in server.DEFAULT_RUNTIME_URL


def test_unreachable_runtime_is_unknown_not_live(monkeypatch):
    monkeypatch.setattr(server, "RUNTIME_URL", f"http://127.0.0.1:{_free_port()}")
    snapshot = server.runtime_snapshot()
    assert snapshot["connected"] is False
    assert snapshot["telemetry_source"] == "UNKNOWN"
    assert snapshot["gate1"] is None
    assert snapshot["gate1_failure"] == "runtime_unreachable"


def test_busy_dashboard_port_is_reported_not_shared():
    # Two dashboards on one port would split requests between them and neither
    # would look wrong enough to notice, so the second bind must fail loudly.
    holder = socket.socket()
    holder.bind(("127.0.0.1", 0))
    holder.listen(1)
    port = holder.getsockname()[1]
    try:
        assert server.serve("127.0.0.1", port) == 1
    finally:
        holder.close()
