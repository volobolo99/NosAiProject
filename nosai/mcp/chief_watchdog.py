from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, TYPE_CHECKING

if TYPE_CHECKING:
    from nosai.mcp.chief import McpChief


def run_health_tick(
    chief: "McpChief",
    log_path: "Path | str",
    *,
    now: "str | None" = None,
) -> "dict[str, Any]":
    """
    Execute a single health‑check tick for the given ``chief`` and append a
    compact JSON record to ``log_path``.

    The function **must not** invoke any mutating methods on ``chief`` – only
    ``health_check`` is called.

    Parameters
    ----------
    chief:
        An instance of :class:`nosai.mcp.chief.McpChief` (or a compatible
        mock) providing a ``health_check`` method.
    log_path:
        Destination file (or path) for the append‑only JSON‑Lines log.
    now:
        Optional ISO‑8601 timestamp to use for the ``ticked_at`` field.
        If omitted the current UTC time is used.

    Returns
    -------
    dict
        The record that has been written to the log.
    """
    # 1. Obtain health information – this is the only allowed call on ``chief``.
    health = chief.health_check()

    # 2. Build the watchdog record.
    ticked_at = now if now is not None else datetime.now(timezone.utc).isoformat()
    record: dict[str, Any] = {
        "schema_version": "mcp.chief.watchdog.v1",
        "ticked_at": ticked_at,
        "status": health.get("status"),
        "failed_checks": health.get("failed_checks", []),
        "recommendations": health.get("recommendations", []),
        # ``provider_health`` may be absent; default to ``None`` if not present.
        "provider_health": health.get("checks", {}).get("provider_health"),
    }

    # 3. Ensure the parent directory exists.
    path_obj = Path(log_path)
    if not path_obj.parent.exists():
        path_obj.parent.mkdir(parents=True, exist_ok=True)

    # 4. Append the JSON line to the log file.
    with path_obj.open("a", encoding="utf-8") as fp:
        json_line = json.dumps(record, ensure_ascii=False)
        fp.write(json_line + "\n")

    # 5. Return the in‑memory record.
    return record


def read_recent_ticks(log_path: "Path | str", limit: int = 20) -> "list[dict[str, Any]]":
    """
    Read the most recent health‑tick records from an append‑only JSON‑Lines log.

    Parameters
    ----------
    log_path:
        Path to the JSON‑Lines file.
    limit:
        Maximum number of records to return. The newest ``limit`` records are
        returned in chronological order (oldest first).

    Returns
    -------
    list[dict]
        A list of parsed tick records; empty if the file does not exist.
    """
    path_obj = Path(log_path)

    if not path_obj.is_file():
        # File missing – contract requires returning an empty list, not raising.
        return []

    # Read all non‑empty lines, decode each as JSON.
    records: list[dict[str, Any]] = []
    with path_obj.open("r", encoding="utf-8") as fp:
        for line in fp:
            stripped = line.strip()
            if not stripped:
                continue
            try:
                records.append(json.loads(stripped))
            except json.JSONDecodeError:
                # Corrupted line – skip it rather than failing the whole read.
                continue

    # The file is chronological; slice the last ``limit`` entries.
    if limit <= 0:
        return []
    recent = records[-limit:] if limit < len(records) else records[:]

    # Ensure chronological order (oldest first). ``records`` already has that order,
    # and slicing from the end preserves it.
    return recent
