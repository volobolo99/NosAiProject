from nosai.mcp.health import HealthSupervisor
from nosai.mcp.state import McpStateStore


def test_missing_provider_observation_is_unknown(tmp_path):
    supervisor = HealthSupervisor(McpStateStore(tmp_path / "state.sqlite3"))
    assert supervisor.snapshot()["observations"] == []
    assert supervisor.status("provider:groq:operational") == "UNKNOWN"


def test_expired_observation_becomes_unknown(tmp_path):
    supervisor = HealthSupervisor(McpStateStore(tmp_path / "state.sqlite3"))
    supervisor.observe(
        "provider:groq:operational",
        "operational",
        {"latency_ms": 42},
        observed_at="2026-09-10T00:00:00+00:00",
        ttl_s=30,
    )
    assert supervisor.status("provider:groq:operational", now="2026-09-10T00:00:31+00:00") == "UNKNOWN"


def test_observations_persist_across_supervisor_restart(tmp_path):
    path = tmp_path / "state.sqlite3"
    first = HealthSupervisor(McpStateStore(path))
    first.observe("provider:local:configured", "configured")
    second = HealthSupervisor(McpStateStore(path))
    assert second.status("provider:local:configured") == "configured"
    assert second.snapshot()["schema_version"] == "mcp.health.v1"
