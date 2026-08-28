"""Transport-neutral JSON-lines protocol for the first PC ↔ phone bring-up.

The transport is intentionally simple: one UTF-8 JSON object per line over a
local Wi-Fi TCP connection. No game integration or privileged action is part
of this protocol.
"""
from __future__ import annotations

from dataclasses import dataclass, asdict
import json
from typing import Any

PROTOCOL_VERSION = "1.0"


@dataclass(frozen=True)
class Message:
    type: str
    session_id: str
    seq: int
    payload: dict[str, Any]
    protocol: str = PROTOCOL_VERSION

    def encode(self) -> bytes:
        return (json.dumps(asdict(self), separators=(",", ":")) + "\n").encode("utf-8")

    @staticmethod
    def decode(line: bytes) -> "Message":
        obj = json.loads(line.decode("utf-8"))
        if obj.get("protocol") != PROTOCOL_VERSION:
            raise ValueError("unsupported protocol version")
        if not isinstance(obj.get("type"), str) or not isinstance(obj.get("session_id"), str):
            raise ValueError("invalid message envelope")
        if not isinstance(obj.get("seq"), int) or not isinstance(obj.get("payload"), dict):
            raise ValueError("invalid message envelope")
        return Message(obj["type"], obj["session_id"], obj["seq"], obj["payload"])


def hello(session_id: str, seq: int, role: str) -> Message:
    return Message("HELLO", session_id, seq, {"role": role, "protocol": PROTOCOL_VERSION})


def capabilities(session_id: str, seq: int, capabilities_list: list[str]) -> Message:
    return Message("CAPABILITIES", session_id, seq, {"capabilities": capabilities_list})


def heartbeat(session_id: str, seq: int) -> Message:
    return Message("HEARTBEAT", session_id, seq, {})


def status(session_id: str, seq: int, state: str, detail: str = "") -> Message:
    return Message("STATUS", session_id, seq, {"state": state, "detail": detail})
