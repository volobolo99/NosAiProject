"""Framing binario PC↔telefono per il protocollo NosAi."""
from __future__ import annotations

import json
import struct
from dataclasses import dataclass
from typing import Any

MAGIC = b"NOSA"
# Version 2 added mutual authentication. A version 1 peer is refused rather than
# downgraded: version 1 cannot prove the runtime to the phone, which is the hole
# the bump exists to close. Source of truth: WireHeader.CurrentVersion.
VERSION = 2
HEADER = struct.Struct(">4sBBHI")  # magic, version, type, payload_len, seq
# PAYLOAD_LEN is a uint16, so 65535 is the largest length the header can express.
# This was 64 * 1024 (= 65536), one byte too generous: a payload of exactly that
# size passed the guard and then failed inside struct.pack with an opaque
# struct.error instead of the intended "payload troppo grande".
# Matches WireHeader.MaxPayloadLength in src/NosAi.Runtime/Gate1/Gate1Runtime.cs.
MAX_PAYLOAD = 0xFFFF

# Canonical Gate 1 message types.
# Source of truth: WireMessageType in src/NosAi.Runtime/Gate1/Gate1Runtime.cs.
# The 12-byte NOSA header above is byte-compatible with that runtime, so these
# identifiers must stay numerically identical on both sides.
TYPE_SESSION_HELLO = 0x01
TYPE_CAPABILITIES = 0x02
TYPE_AUTH_CHALLENGE = 0x03
TYPE_AUTH_RESPONSE = 0x04
TYPE_AUTH_RESULT = 0x05
TYPE_SERVER_AUTH_PROOF = 0x08
TYPE_HEARTBEAT = 0x06
TYPE_HEARTBEAT_ACK = 0x07
TYPE_WORLD_STATE_DELTA = 0x10
TYPE_TELEMETRY_SNAPSHOT = 0x11
TYPE_COMMAND_REQUEST = 0x20
TYPE_COMMAND_ACK = 0x21
TYPE_DISCONNECT = 0xFF

KNOWN_MESSAGE_TYPES = frozenset({
    TYPE_SESSION_HELLO, TYPE_CAPABILITIES, TYPE_AUTH_CHALLENGE, TYPE_AUTH_RESPONSE,
    TYPE_AUTH_RESULT, TYPE_SERVER_AUTH_PROOF, TYPE_HEARTBEAT, TYPE_HEARTBEAT_ACK, TYPE_WORLD_STATE_DELTA,
    TYPE_TELEMETRY_SNAPSHOT, TYPE_COMMAND_REQUEST, TYPE_COMMAND_ACK, TYPE_DISCONNECT,
})


@dataclass(frozen=True)
class Frame:
    message_type: int
    sequence: int
    payload: bytes = b""

    def encode(self) -> bytes:
        if not 0 <= self.message_type <= 255:
            raise ValueError("message_type fuori intervallo")
        if not 0 <= self.sequence <= 0xFFFFFFFF:
            raise ValueError("sequence fuori intervallo")
        if len(self.payload) > MAX_PAYLOAD:
            raise ValueError("payload troppo grande")
        return HEADER.pack(MAGIC, VERSION, self.message_type, len(self.payload), self.sequence) + self.payload


def decode(data: bytes) -> Frame:
    if len(data) < HEADER.size:
        raise ValueError("intestazione frame incompleta")
    magic, version, message_type, length, sequence = HEADER.unpack(data[:HEADER.size])
    if magic != MAGIC or version != VERSION:
        raise ValueError("intestazione frame non valida")
    if length > MAX_PAYLOAD or len(data) != HEADER.size + length:
        raise ValueError("lunghezza payload non valida")
    return Frame(message_type, sequence, data[HEADER.size:])


class SequenceGuard:
    """Accetta esclusivamente la sequenza monotona successiva."""

    def __init__(self, expected: int = 1):
        self.expected = expected

    def accept(self, sequence: int) -> bool:
        if sequence != self.expected:
            return False
        self.expected += 1
        return True


def encode_delta(previous: dict[str, Any], current: dict[str, Any]) -> bytes:
    """Serializza solo i campi mutati rispetto allo stato precedente.

    Il risultato è deterministico e limitato a JSON UTF-8; la compressione e la
    cifratura possono essere applicate dal livello di trasporto senza duplicare
    la logica di confronto.
    """
    changed = {key: current[key] for key in sorted(current) if previous.get(key) != current[key]}
    removed = sorted(key for key in previous if key not in current)
    payload = {"changed": changed, "removed": removed}
    encoded = json.dumps(payload, ensure_ascii=False, separators=(",", ":"), sort_keys=True).encode("utf-8")
    if len(encoded) > MAX_PAYLOAD:
        raise ValueError("delta troppo grande")
    return encoded


def apply_delta(previous: dict[str, Any], delta: bytes) -> dict[str, Any]:
    """Applica un delta senza modificare il dizionario di origine."""
    payload = json.loads(delta.decode("utf-8"))
    if not isinstance(payload, dict) or not isinstance(payload.get("changed"), dict) or not isinstance(payload.get("removed"), list):
        raise ValueError("delta non valido")
    result = dict(previous)
    for key in payload["removed"]:
        if not isinstance(key, str):
            raise ValueError("chiave rimossa non valida")
        result.pop(key, None)
    for key, value in payload["changed"].items():
        if not isinstance(key, str):
            raise ValueError("chiave modificata non valida")
        result[key] = value
    return result
