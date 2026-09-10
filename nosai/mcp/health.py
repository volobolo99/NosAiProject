from __future__ import annotations

from datetime import datetime
from typing import Any, Mapping

from .state import McpStateStore


HEALTH_STATES = frozenset({"configured", "reachable", "authenticated", "qualified", "operational", "UNKNOWN"})


class HealthSupervisor:
    """Deterministic, persisted health observations for MCP providers and workers."""

    def __init__(self, state: McpStateStore) -> None:
        self.state = state

    def observe(
        self,
        name: str,
        status: str,
        details: Mapping[str, Any] | None = None,
        *,
        observed_at: str | None = None,
        ttl_s: int | None = None,
    ) -> dict[str, Any]:
        normalized = str(status).strip()
        if normalized not in HEALTH_STATES - {"UNKNOWN"}:
            raise ValueError("status must be configured, reachable, authenticated, qualified or operational")
        return self.state.observe(
            name,
            normalized,
            details,
            observed_at=observed_at,
            ttl_s=ttl_s,
        )

    def status(self, name: str, *, now: str | None = None) -> str:
        for observation in self.snapshot(now=now)["observations"]:
            if observation["name"] == name:
                return str(observation["status"])
        return "UNKNOWN"

    def snapshot(self, *, now: str | None = None) -> dict[str, Any]:
        return {
            "schema_version": "mcp.health.v1",
            "observations": self.state.observation_snapshot(now=now),
            "state_revision": self.state.snapshot()["revision"],
        }

    def operational_provider_ids(self, *, now: str | None = None) -> set[str]:
        result: set[str] = set()
        for observation in self.snapshot(now=now)["observations"]:
            prefix = "provider:"
            suffix = ":operational"
            name = str(observation["name"])
            if name.startswith(prefix) and name.endswith(suffix) and observation["status"] == "operational":
                result.add(name[len(prefix) : -len(suffix)])
        return result
