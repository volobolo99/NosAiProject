"""Binary PC↔Phone framing defined by the NosAi onboarding specification."""
from __future__ import annotations

import struct
from dataclasses import dataclass

MAGIC = b"NOSA"
VERSION = 1
HEADER = struct.Struct(">4sBBHI")  # magic, version, type, payload_len, seq
MAX_PAYLOAD = 64 * 1024

@dataclass(frozen=True)
class Frame:
    message_type: int
    sequence: int
    payload: bytes = b""

    def encode(self) -> bytes:
        if not 0 <= self.message_type <= 255:
            raise ValueError("message_type out of range")
        if not 0 <= self.sequence <= 0xFFFFFFFF:
            raise ValueError("sequence out of range")
        if len(self.payload) > MAX_PAYLOAD:
            raise ValueError("payload too large")
        return HEADER.pack(MAGIC, VERSION, self.message_type, len(self.payload), self.sequence) + self.payload


def decode(data: bytes) -> Frame:
    if len(data) < HEADER.size:
        raise ValueError("incomplete frame header")
    magic, version, message_type, length, sequence = HEADER.unpack(data[:HEADER.size])
    if magic != MAGIC or version != VERSION:
        raise ValueError("invalid frame header")
    if length > MAX_PAYLOAD or len(data) != HEADER.size + length:
        raise ValueError("invalid payload length")
    return Frame(message_type, sequence, data[HEADER.size:])


class SequenceGuard:
    """Accept only the next monotonic sequence; caller must fail closed on violation."""
    def __init__(self, expected: int = 1):
        self.expected = expected

    def accept(self, sequence: int) -> bool:
        if sequence != self.expected:
            return False
        self.expected += 1
        return True
