"""Records a Claude Code tool call into the traffic log the inspector follows.

Claude delegates to DeepSeek through an MCP server that is not a file in this
repository, so it cannot be instrumented the way `orchestrator_mcp.py` is. A
hook can see those calls, and this script is what the hook runs: it reads the
hook payload on standard input and appends one line to the same log the
inspector tails, so a delegation Claude starts shows up next to the ones the
orchestrator makes.

Usage: `python hook_event.py start|end`

Never fails loudly: a hook that errors would interrupt real work for the sake
of a dashboard line.
"""

from __future__ import annotations

import hashlib
import json
import pathlib
import sys
import time

LOG = pathlib.Path(__file__).resolve().parents[2] / "data" / "traffic" / "events.jsonl"


def call_id(payload: dict) -> str:
    """The same id for the start and the end of one call.

    Derived from the tool input, which is identical in both hook events, so the
    page updates the row it already shows instead of adding a second one. The
    session id would not do: it is the same for every call in the session.
    """
    material = json.dumps(payload.get("tool_input") or {}, sort_keys=True, default=str)
    return hashlib.sha1(material.encode("utf-8", "replace")).hexdigest()[:12]


def succeeded(payload: dict) -> bool:
    response = payload.get("tool_response")
    if isinstance(response, dict):
        if response.get("is_error") or response.get("error"):
            return False
    return True


def main() -> int:
    phase = sys.argv[1] if len(sys.argv) > 1 else "start"
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        payload = {}

    identifier = call_id(payload)
    now = time.time()
    event = {
        "id": identifier,
        "at": now,
        "agent": "deepseek",
        "agentName": "DeepSeek Flash",
        "model": "deepseek-v4-flash",
        "op": "delegate_to_deepseek",
        "phase": phase,
    }

    # A delegation runs for minutes, so its duration is the number worth having.
    # The end hook cannot know when the call started, so the start hook leaves a
    # stamp behind and the end hook consumes it.
    stamp = LOG.parent / "pending" / identifier
    if phase == "start":
        try:
            stamp.parent.mkdir(parents=True, exist_ok=True)
            stamp.write_text(str(now), encoding="utf-8")
        except OSError:
            pass
    else:
        event["ok"] = succeeded(payload)
        try:
            started = float(stamp.read_text(encoding="utf-8"))
            event["durationMs"] = int((now - started) * 1000)
            stamp.unlink()
        except (OSError, ValueError):
            pass  # no stamp: report the call without a duration, never a wrong one

    try:
        LOG.parent.mkdir(parents=True, exist_ok=True)
        with LOG.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(event, ensure_ascii=False) + "\n")
    except OSError:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
