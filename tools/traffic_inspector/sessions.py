"""What every Claude Code session is doing right now, read from its transcript.

The inspector used to report only that a session had touched its file. That
answers "is someone working" and nothing else. This module reads the transcript
itself - the tool being run, the last thing the session said, the tokens it has
burned - because the console has to show what each agent is doing, not that it
exists.

Reading is incremental: each transcript keeps its own offset, so a poll costs
the bytes appended since the previous one. A transcript already larger than
BIG_FILE_BYTES when first seen is picked up from its tail; its counters then
carry the note that they start there, since a wrong total is worse than a total
that says where it begins (CLAUDE.md 6).
"""

from __future__ import annotations

import calendar
import json
import time
from pathlib import Path

# A transcript this big is not read from the start: 26 MB of history would stall
# the first poll and the interesting part is the end of it anyway.
BIG_FILE_BYTES = 8 * 1024 * 1024
TAIL_BYTES = 512 * 1024

# Beyond this a session is no longer "working": it is idle, not gone.
ACTIVE_SECONDS = 90.0
# And beyond this it does not belong on the board at all.
LISTED_SECONDS = 12 * 3600.0


def _text_of(content) -> str:
    """The text a message carries, whatever shape the content has."""
    if isinstance(content, str):
        return content
    if not isinstance(content, list):
        return ""
    parts = []
    for block in content:
        if isinstance(block, dict) and block.get("type") == "text":
            parts.append(block.get("text") or "")
    return "\n".join(p for p in parts if p)


def _describe_tool(name: str, args: dict) -> str:
    """One line saying what this tool call is actually doing."""
    if not isinstance(args, dict):
        return name
    if name in ("Bash", "PowerShell"):
        return args.get("description") or (args.get("command") or "")[:110]
    if name in ("Read", "Edit", "Write", "NotebookEdit"):
        return (args.get("file_path") or "")[-70:]
    if name in ("Grep", "Glob"):
        return (args.get("pattern") or "")[:70]
    if name in ("Task", "Agent"):
        return (args.get("subagent_type") or "") + ": " + (args.get("description") or "")[:60]
    if name == "TodoWrite":
        return "aggiorna la lista di lavoro"
    if name.startswith("mcp__"):
        return name.split("__", 2)[-1]
    return (args.get("description") or args.get("prompt") or "")[:70]


class Transcript:
    """One session file, followed from where the last read stopped."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.session_id = path.stem
        self.offset = 0
        self.partial = True          # counters do not cover the whole history
        self.title = ""
        self.agent_setting = ""
        self.model = ""
        self.cwd = ""
        self.git_branch = ""
        self.activity = ""           # what the last tool call is doing
        self.activity_tool = ""
        self.said = ""               # last thing the session said
        self.asked = ""              # last thing the user asked it
        self.last_at = 0.0
        self.turns = 0
        self.tools = 0
        self.input_tokens = 0
        self.output_tokens = 0
        self.cache_read_tokens = 0
        self.cache_write_tokens = 0
        self.cost_usd = None         # only ever the figure the transcript states
        self.is_sidechain = False

    def _seed(self, size: int) -> None:
        if size > BIG_FILE_BYTES:
            self.offset = max(0, size - TAIL_BYTES)
        else:
            self.offset = 0
            self.partial = False

    def poll(self) -> bool:
        """Reads what was appended. True when something changed."""
        try:
            stat = self.path.stat()
        except OSError:
            return False
        if self.offset == 0 and self.partial:
            self._seed(stat.st_size)
        if stat.st_size < self.offset:      # truncated or replaced
            self.offset = 0
            self.partial = False
        if stat.st_size == self.offset:
            return False
        try:
            with self.path.open("r", encoding="utf-8", errors="replace") as handle:
                handle.seek(self.offset)
                lines = handle.readlines()
                self.offset = handle.tell()
        except OSError:
            return False
        # A line still being written has no newline yet: leave it for next time.
        if lines and not lines[-1].endswith("\n"):
            self.offset -= len(lines[-1].encode("utf-8", "replace"))
            lines = lines[:-1]
        changed = False
        for line in lines:
            line = line.strip()
            if not line:
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            changed = self._absorb(row) or changed
        return changed

    def _absorb(self, row: dict) -> bool:
        kind = row.get("type")
        if row.get("cwd"):
            self.cwd = row["cwd"]
        if row.get("gitBranch"):
            self.git_branch = row["gitBranch"]
        if row.get("isSidechain"):
            self.is_sidechain = True

        if kind == "ai-title" and row.get("aiTitle"):
            self.title = row["aiTitle"]
            return True
        if kind == "agent-name" and row.get("agentName"):
            self.title = self.title or row["agentName"]
            return True
        if kind == "agent-setting" and row.get("agentSetting"):
            self.agent_setting = row["agentSetting"]
            return True
        if kind == "cost-state":
            if isinstance(row.get("totalCostUSD"), (int, float)):
                self.cost_usd = row["totalCostUSD"]
            return True

        stamp = row.get("timestamp")
        if isinstance(stamp, str):
            try:
                # 2026-09-09T08:12:02.514Z - UTC, so timegm and not mktime:
                # mktime would read it as local time and shift the clock by the
                # timezone offset.
                struct = time.strptime(stamp[:19], "%Y-%m-%dT%H:%M:%S")
                self.last_at = calendar.timegm(struct)
            except ValueError:
                pass

        if kind == "user":
            message = row.get("message") or {}
            if row.get("isMeta") or row.get("attachment"):
                return False
            text = _text_of(message.get("content"))
            if text and not text.startswith("<"):
                self.asked = text[:400]
                return True
            return False

        if kind != "assistant":
            return False

        message = row.get("message") or {}
        if message.get("model"):
            self.model = message["model"]
        usage = message.get("usage") or {}
        self.input_tokens += int(usage.get("input_tokens") or 0)
        self.output_tokens += int(usage.get("output_tokens") or 0)
        self.cache_read_tokens += int(usage.get("cache_read_input_tokens") or 0)
        self.cache_write_tokens += int(usage.get("cache_creation_input_tokens") or 0)
        self.turns += 1
        for block in message.get("content") or []:
            if not isinstance(block, dict):
                continue
            if block.get("type") == "tool_use":
                self.tools += 1
                self.activity_tool = block.get("name") or ""
                self.activity = _describe_tool(self.activity_tool, block.get("input") or {})
            elif block.get("type") == "text" and (block.get("text") or "").strip():
                self.said = block["text"].strip()[:400]
                self.activity_tool = self.activity_tool or ""
        return True

    def state(self, now: float) -> dict:
        idle = now - max(self.last_at, self._mtime())
        return {
            "id": "claude:" + self.session_id,
            "kind": "claude-session",
            "sessionId": self.session_id,
            "name": self.title or self.session_id[:8],
            "status": "ATTIVO" if idle <= ACTIVE_SECONDS else "IN PAUSA",
            "model": self.model,
            "agentSetting": self.agent_setting,
            "activity": self.activity,
            "activityTool": self.activity_tool,
            "said": self.said,
            "asked": self.asked,
            "branch": self.git_branch,
            "cwd": self.cwd,
            "turns": self.turns,
            "tools": self.tools,
            "inputTokens": self.input_tokens,
            "outputTokens": self.output_tokens,
            "cacheReadTokens": self.cache_read_tokens,
            "cacheWriteTokens": self.cache_write_tokens,
            "costUsd": self.cost_usd,
            "partial": self.partial,
            "sidechain": self.is_sidechain,
            "lastSeen": max(self.last_at, self._mtime()),
            "commandable": True,
        }

    def _mtime(self) -> float:
        try:
            return self.path.stat().st_mtime
        except OSError:
            return 0.0


class SessionBoard:
    """Every recent transcript in the project folder, followed together."""

    def __init__(self, folder: Path) -> None:
        self.folder = folder
        self.tracked: dict[str, Transcript] = {}

    def poll(self) -> list[dict]:
        now = time.time()
        if not self.folder.is_dir():
            return []
        for path in self.folder.glob("*.jsonl"):
            try:
                if now - path.stat().st_mtime > LISTED_SECONDS:
                    continue
            except OSError:
                continue
            transcript = self.tracked.get(path.stem)
            if transcript is None:
                transcript = Transcript(path)
                self.tracked[path.stem] = transcript
            transcript.poll()
        states = []
        for transcript in self.tracked.values():
            state = transcript.state(now)
            if now - state["lastSeen"] <= LISTED_SECONDS:
                states.append(state)
        states.sort(key=lambda s: s["lastSeen"], reverse=True)
        return states
