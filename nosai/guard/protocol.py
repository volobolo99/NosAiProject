"""Minimal PC Guard <-> phone Guard AI protocol.

Transport-independent on purpose: TCP/WebSocket/BLE can be plugged in later.
"""

from dataclasses import dataclass
from enum import Enum


class MessageType(str, Enum):
    HELLO = "HELLO"
    HEARTBEAT = "HEARTBEAT"
    STATUS = "STATUS"
    CAPABILITIES = "CAPABILITIES"
    ACK = "ACK"


@dataclass(frozen=True)
class GuardMessage:
    message_type: MessageType
    session_id: str
    sequence: int
    payload: dict[str, object]


@dataclass(frozen=True)
class GuardEndpoint:
    name: str
    role: str
    version: str


def make_hello(endpoint: GuardEndpoint, session_id: str) -> GuardMessage:
    return GuardMessage(
        message_type=MessageType.HELLO,
        session_id=session_id,
        sequence=0,
        payload={"name": endpoint.name, "role": endpoint.role, "version": endpoint.version},
    )


def make_heartbeat(session_id: str, sequence: int, uptime_ms: int) -> GuardMessage:
    return GuardMessage(
        message_type=MessageType.HEARTBEAT,
        session_id=session_id,
        sequence=sequence,
        payload={"uptime_ms": uptime_ms},
    )
