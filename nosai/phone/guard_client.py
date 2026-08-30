"""Reference Guard AI client for the canonical PC↔phone channel.

ADR-0006 makes `GuardAiNetworkChannel` (NOSA binary framing, RSA-2048
challenge/response, TCP/17471) the only canonical channel, and requires the
phone-side Guard AI application to implement exactly that contract. Until this
module existed the contract had never been exercised by an independent client:
the runtime's own suite hand-rolled frames inline, so it could only ever agree
with itself.

This is the executable reference for that contract. It is deliberately a plain
client with no phone dependencies, so it can be:

- run against the real runtime as a conformance check;
- read as the normative description of the handshake;
- ported to the phone application without re-deriving the wire format.

It is NOT the Guard AI application, and running it does not close any Gate 1
smartphone checklist row.

Session contract, from the client's side:

    1. connect
    2. send    SESSION_HELLO      (empty)
    3. receive CAPABILITIES       (UTF-8 descriptor)
    4. receive AUTH_CHALLENGE     (32 random bytes)
    5. send    AUTH_RESPONSE      (RSA-2048/SHA-256/PKCS#1 v1.5 over the challenge)
    6. receive AUTH_RESULT        (1 byte: 1 accepted, 0 refused)
    7. receive TELEMETRY_SNAPSHOT (Gate 1 classified snapshot, JSON)
    8. send    HEARTBEAT -> receive HEARTBEAT_ACK + TELEMETRY_SNAPSHOT

Both directions are sequence-guarded independently, each starting at 1. The
server terminates a session that misses its heartbeat for 2000 ms, so callers
must heartbeat well inside `HEARTBEAT_TIMEOUT_MS`.
"""
from __future__ import annotations

import json
import socket
from dataclasses import dataclass
from typing import Any

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

from nosai.network.wire_protocol import (
    HEADER,
    MAX_PAYLOAD,
    SequenceGuard,
    TYPE_AUTH_CHALLENGE,
    TYPE_AUTH_RESPONSE,
    TYPE_AUTH_RESULT,
    TYPE_CAPABILITIES,
    TYPE_DISCONNECT,
    TYPE_HEARTBEAT,
    TYPE_HEARTBEAT_ACK,
    TYPE_SESSION_HELLO,
    TYPE_TELEMETRY_SNAPSHOT,
    Frame,
    decode,
)

#: Server-side heartbeat deadline. Source of truth:
#: GuardAiNetworkChannel.HeartbeatTimeout in Gate1Runtime.cs.
HEARTBEAT_TIMEOUT_MS = 2000


class GuardProtocolError(RuntimeError):
    """The peer violated the canonical contract.

    Carries a structured `reason` so a caller can distinguish a refused
    authentication from a framing or sequencing violation, rather than reading
    an English sentence.
    """

    def __init__(self, reason: str, detail: str | None = None):
        super().__init__(f"{reason}: {detail}" if detail else reason)
        self.reason = reason
        self.detail = detail


@dataclass(frozen=True)
class GuardSession:
    """Result of a completed handshake."""

    capabilities: str
    authenticated: bool
    telemetry: dict[str, Any]


class GuardAiClient:
    """Speaks the canonical Gate 1 channel as the phone side would.

    The private key never leaves the client, mirroring the real deployment: the
    runtime holds only the trusted public key and can verify but not sign.
    """

    def __init__(
        self,
        host: str,
        port: int,
        private_key: rsa.RSAPrivateKey,
        timeout: float = 5.0,
    ):
        if private_key.key_size != 2048:
            raise ValueError("Gate 1 accepts RSA-2048 keys only")
        self._host = host
        self._port = port
        self._key = private_key
        self._timeout = timeout
        self._sock: socket.socket | None = None
        # Independent guards per direction, both starting at 1, matching the
        # runtime's _ingress/_egress pair.
        self._egress = SequenceGuard(1)
        self._ingress = SequenceGuard(1)

    # -- lifecycle -----------------------------------------------------------

    def __enter__(self) -> GuardAiClient:
        self.connect()
        return self

    def __exit__(self, *_exc: object) -> None:
        self.close()

    def connect(self) -> None:
        if self._sock is not None:
            raise GuardProtocolError("already_connected")
        self._sock = socket.create_connection((self._host, self._port), timeout=self._timeout)

    def close(self) -> None:
        """Best-effort DISCONNECT, then drop the socket.

        A failure to announce the disconnect is not raised: the peer detects the
        closed socket regardless, and masking the original error would be worse.
        """
        sock = self._sock
        if sock is None:
            return
        try:
            self._send(TYPE_DISCONNECT)
        except (OSError, GuardProtocolError):
            pass
        finally:
            self._sock = None
            try:
                sock.close()
            except OSError:
                pass

    # -- framing -------------------------------------------------------------

    def _send(self, message_type: int, payload: bytes = b"") -> None:
        sock = self._require_socket()
        if len(payload) > MAX_PAYLOAD:
            raise GuardProtocolError("payload_too_large", str(len(payload)))
        sequence = self._egress.expected
        frame = Frame(message_type=message_type, sequence=sequence, payload=payload)
        try:
            sock.sendall(frame.encode())
        except OSError as exc:
            raise GuardProtocolError("send_failed", exc.strerror or str(exc)) from exc
        # Advance only after the bytes are away, so a failed send does not burn a
        # sequence number the server will then see as a gap.
        self._egress.accept(sequence)

    def _receive(self) -> Frame:
        header = self._read_exactly(HEADER.size)
        _magic, _version, _type, length, _sequence = HEADER.unpack(header)
        if length > MAX_PAYLOAD:
            raise GuardProtocolError("payload_too_large", str(length))
        payload = self._read_exactly(length) if length else b""
        try:
            frame = decode(header + payload)
        except ValueError as exc:
            raise GuardProtocolError("invalid_frame", str(exc)) from exc
        if not self._ingress.accept(frame.sequence):
            raise GuardProtocolError(
                "sequence_violation",
                f"expected {self._ingress.expected}, received {frame.sequence}",
            )
        return frame

    def _read_exactly(self, count: int) -> bytes:
        sock = self._require_socket()
        chunks: list[bytes] = []
        remaining = count
        while remaining > 0:
            try:
                chunk = sock.recv(remaining)
            except socket.timeout as exc:
                raise GuardProtocolError("receive_timeout") from exc
            except OSError as exc:
                raise GuardProtocolError("receive_failed", exc.strerror or str(exc)) from exc
            if not chunk:
                raise GuardProtocolError("peer_disconnected")
            chunks.append(chunk)
            remaining -= len(chunk)
        return b"".join(chunks)

    def _expect(self, message_type: int) -> Frame:
        frame = self._receive()
        if frame.message_type != message_type:
            raise GuardProtocolError(
                "unexpected_message_type",
                f"expected 0x{message_type:02X}, received 0x{frame.message_type:02X}",
            )
        return frame

    def _require_socket(self) -> socket.socket:
        if self._sock is None:
            raise GuardProtocolError("not_connected")
        return self._sock

    # -- session -------------------------------------------------------------

    def open_session(self) -> GuardSession:
        """Run the full handshake and return the first telemetry snapshot.

        Raises `GuardProtocolError("authentication_refused")` when the runtime
        rejects the signature. That is a fail-closed outcome, not a soft result:
        the runtime terminates the session immediately afterwards, so returning
        an unauthenticated session object would invite the caller to keep using
        a dead socket.
        """
        self._send(TYPE_SESSION_HELLO)
        capabilities = self._expect(TYPE_CAPABILITIES).payload.decode("utf-8")
        challenge = self._expect(TYPE_AUTH_CHALLENGE).payload
        if len(challenge) != 32:
            raise GuardProtocolError("invalid_challenge_length", str(len(challenge)))

        self._send(TYPE_AUTH_RESPONSE, self.sign_challenge(challenge))
        result = self._expect(TYPE_AUTH_RESULT).payload
        if result != b"\x01":
            raise GuardProtocolError("authentication_refused")

        telemetry = self._read_telemetry()
        return GuardSession(capabilities=capabilities, authenticated=True, telemetry=telemetry)

    def heartbeat(self) -> dict[str, Any]:
        """Send one heartbeat and return the telemetry snapshot that follows it."""
        self._send(TYPE_HEARTBEAT)
        self._expect(TYPE_HEARTBEAT_ACK)
        return self._read_telemetry()

    def sign_challenge(self, challenge: bytes) -> bytes:
        """RSA-2048 / SHA-256 / PKCS#1 v1.5, matching SessionAuth.VerifyAndConsume."""
        return self._key.sign(challenge, padding.PKCS1v15(), hashes.SHA256())

    def _read_telemetry(self) -> dict[str, Any]:
        payload = self._expect(TYPE_TELEMETRY_SNAPSHOT).payload
        try:
            snapshot = json.loads(payload.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise GuardProtocolError("invalid_telemetry", str(exc)) from exc
        if not isinstance(snapshot, dict):
            raise GuardProtocolError("invalid_telemetry", "snapshot is not an object")
        return snapshot


def public_key_pem(private_key: rsa.RSAPrivateKey) -> bytes:
    """The trusted public key to hand the runtime.

    SubjectPublicKeyInfo ("BEGIN PUBLIC KEY"). RSA.ImportFromPem on the runtime
    side accepts this as well as PKCS#1, so either encoding enrolls correctly.
    """
    return private_key.public_key().public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )


def generate_client_key() -> rsa.RSAPrivateKey:
    """Generate a Gate 1-acceptable client key. Test and enrollment helper."""
    return rsa.generate_private_key(public_exponent=65537, key_size=2048)


__all__ = [
    "GuardAiClient",
    "GuardProtocolError",
    "GuardSession",
    "HEARTBEAT_TIMEOUT_MS",
    "generate_client_key",
    "public_key_pem",
]
