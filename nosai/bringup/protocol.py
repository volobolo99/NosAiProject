"""Protocollo locale tipizzato per il coordinamento PC ↔ telefono."""
from __future__ import annotations
from dataclasses import dataclass, asdict
import json
import secrets
from typing import Any

PROTOCOL_VERSION = "1.1"
NONCE_BYTES = 24

@dataclass(frozen=True)
class Message:
    type: str
    session_id: str
    seq: int
    payload: dict[str, Any]
    protocol: str = PROTOCOL_VERSION
    nonce: str = ""

    def encode(self) -> bytes:
        return (json.dumps(asdict(self), separators=(",", ":")) + "\n").encode("utf-8")

    @staticmethod
    def decode(line: bytes) -> "Message":
        obj = json.loads(line.decode("utf-8"))
        if obj.get("protocol") != PROTOCOL_VERSION:
            raise ValueError("versione protocollo non supportata")
        if not isinstance(obj.get("type"), str) or not isinstance(obj.get("session_id"), str):
            raise ValueError("busta messaggio non valida")
        if not isinstance(obj.get("seq"), int) or obj["seq"] < 0 or not isinstance(obj.get("payload"), dict):
            raise ValueError("busta messaggio non valida")
        nonce = obj.get("nonce")
        if not isinstance(nonce, str) or len(nonce) < 16:
            raise ValueError("nonce mancante o non valido")
        return Message(obj["type"], obj["session_id"], obj["seq"], obj["payload"], obj["protocol"], nonce)

def _message(type_: str, session_id: str, seq: int, payload: dict[str, Any]) -> Message:
    return Message(type_, session_id, seq, payload, nonce=secrets.token_hex(NONCE_BYTES))

def hello(session_id: str, seq: int, role: str) -> Message:
    return _message("HELLO", session_id, seq, {"role": role, "protocol": PROTOCOL_VERSION})

def capabilities(session_id: str, seq: int, capabilities_list: list[str]) -> Message:
    return _message("CAPABILITIES", session_id, seq, {"capabilities": capabilities_list})

def heartbeat(session_id: str, seq: int) -> Message:
    return _message("HEARTBEAT", session_id, seq, {})

def status(session_id: str, seq: int, state: str, detail: str = "") -> Message:
    return _message("STATUS", session_id, seq, {"state": state, "detail": detail})
