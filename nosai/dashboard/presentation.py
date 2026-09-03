"""Truthful, display-ready fields derived from a Gate 1 snapshot."""
from __future__ import annotations

from typing import Any


def flatten_gate1_observations(snapshot: dict[str, Any]) -> list[dict[str, Any]]:
    """Flatten every snapshot leaf while preserving its observation metadata.

    Gate 1 wraps measured values in a classified envelope.  Child values such as
    entity rows inherit that envelope when the wire format has no per-cell
    provenance.  Unclassified structural fields remain explicitly
    ``UNCLASSIFIED`` rather than being mislabelled as live data.
    """
    fields: list[dict[str, Any]] = []

    def classified(node: Any) -> bool:
        return isinstance(node, dict) and "value" in node and "source" in node

    def append(path: str, value: Any, metadata: dict[str, Any] | None) -> None:
        fields.append({
            "path": path,
            "value": value,
            "source": metadata.get("source", "UNCLASSIFIED") if metadata else "UNCLASSIFIED",
            "observed_at_utc": metadata.get("observedAtUtc") if metadata else None,
            "failure_reason": metadata.get("failureReason") if metadata else None,
        })

    def visit(node: Any, path: str, inherited: dict[str, Any] | None = None) -> None:
        if classified(node):
            value = node["value"]
            if isinstance(value, (dict, list)):
                visit(value, path, node)
            else:
                append(path, value, node)
            return

        if isinstance(node, dict):
            for key, value in node.items():
                visit(value, f"{path}.{key}" if path else str(key), inherited)
            return

        if isinstance(node, list):
            if not node:
                append(path, [], inherited)
                return
            for index, value in enumerate(node):
                visit(value, f"{path}[{index}]", inherited)
            return

        append(path, node, inherited)

    visit(snapshot, "")
    return fields
