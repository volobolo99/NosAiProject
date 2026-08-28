"""Deterministic local/LAN session protocol primitives."""
from dataclasses import dataclass
from enum import Enum

class MessageType(str, Enum):
    HELLO="HELLO"; CAPABILITIES="CAPABILITIES"; AUTH="AUTH"; HEARTBEAT="HEARTBEAT"
    STATUS="STATUS"; EVENT="EVENT"; COMMAND="COMMAND"; ACK="ACK"; ERROR="ERROR"; DISCONNECT="DISCONNECT"

@dataclass(frozen=True)
class SessionMessage:
    session_id: str
    sequence: int
    type: MessageType
    payload: dict[str, object]

class SequenceGuard:
    """Reject duplicate/out-of-order messages; no transport dependency."""
    def __init__(self): self._last: dict[str, int] = {}
    def accept(self, message: SessionMessage) -> bool:
        last = self._last.get(message.session_id, -1)
        if message.sequence <= last: return False
        self._last[message.session_id] = message.sequence
        return True
