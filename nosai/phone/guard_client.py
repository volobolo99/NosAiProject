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

Session contract, from the client's side (wire version 3):

    1. connect
    2. send    SESSION_HELLO      (32-byte client nonce + 65-byte ephemeral key)
    3. receive CAPABILITIES       (UTF-8 descriptor)
    4. receive AUTH_CHALLENGE     (32-byte server nonce + 65-byte ephemeral key)
    5. receive SERVER_AUTH_PROOF  (runtime signature over the server transcript)
    6. send    AUTH_RESPONSE      (phone signature over the client transcript)
    7. receive AUTH_RESULT        (1 byte: 1 accepted, 0 refused)
    8. receive TELEMETRY_SNAPSHOT (Gate 1 classified snapshot, JSON, encrypted)
    9. send    HEARTBEAT -> receive HEARTBEAT_ACK + TELEMETRY_SNAPSHOT

Both signatures cover `nosai.network.session_transcript`, not the raw nonce.
The runtime public key is pinned at pairing; without it the handshake is
fail-closed. Both directions are sequence-guarded independently, each starting
at 1. The server terminates a session that misses its heartbeat for 2000 ms, so
callers must heartbeat well inside `HEARTBEAT_TIMEOUT_MS`.

Everything after the handshake is sealed with AES-256-GCM under keys derived
from the ephemeral exchange, which those same signatures authenticate
(ADR-0009). A non-handshake frame that arrives in clear is refused, not read.
"""
from __future__ import annotations

import json
import socket
from dataclasses import dataclass
from typing import Any

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric.utils import Prehashed
from cryptography.hazmat.primitives.asymmetric import padding, rsa

from nosai.network.session_cipher import (
    SessionCipher,
    SessionCipherError,
    derive_session_material,
    generate_ephemeral,
    public_key_bytes,
)
from nosai.network.session_transcript import (
    EPHEMERAL_KEY_LENGTH,
    NONCE_LENGTH,
    ROLE_CLIENT,
    ROLE_SERVER,
    compute,
    compute_binding,
    create_nonce,
)
from nosai.network.wire_protocol import (
    HEADER,
    MAX_PAYLOAD,
    SequenceGuard,
    is_handshake,
    TYPE_AUTH_CHALLENGE,
    TYPE_AUTH_RESPONSE,
    TYPE_AUTH_RESULT,
    TYPE_CAPABILITIES,
    TYPE_DISCONNECT,
    TYPE_HEARTBEAT,
    TYPE_HEARTBEAT_ACK,
    TYPE_SERVER_AUTH_PROOF,
    TYPE_SESSION_HELLO,
    TYPE_TELEMETRY_SNAPSHOT,
    MAGIC,
    VERSION,
    Frame,
    decode,
)
from nosai.network.session_cipher import OVERHEAD

#: Server-side heartbeat deadline. Source of truth:
#: GuardAiNetworkChannel.HeartbeatTimeout in Gate1Runtime.cs.
HEARTBEAT_TIMEOUT_MS = 2000

#: Length of the hello each side sends. Source of truth:
#: SessionAuth.HandshakeHelloLength in Gate1Runtime.cs.
HANDSHAKE_HELLO_LENGTH = NONCE_LENGTH + EPHEMERAL_KEY_LENGTH


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
        runtime_public_key: rsa.RSAPublicKey,
        timeout: float = 5.0,
    ):
        """
        :param runtime_public_key: the runtime's key, pinned during USB pairing.

        Required, not optional. Without it the phone cannot tell a genuine runtime
        from anything else that answered on the network, which is the whole reason
        version 2 exists.
        """
        if private_key.key_size != 2048:
            raise ValueError("Gate 1 accepts RSA-2048 keys only")
        if runtime_public_key.key_size != 2048:
            raise ValueError("the runtime key must be RSA-2048")
        self._host = host
        self._port = port
        self._key = private_key
        self._runtime_key = runtime_public_key
        self._timeout = timeout
        self._sock: socket.socket | None = None
        # Independent guards per direction, both starting at 1, matching the
        # runtime's _ingress/_egress pair.
        self._egress = SequenceGuard(1)
        self._ingress = SequenceGuard(1)
        # None until the handshake completes, which is why a non-handshake frame
        # before that point is refused rather than read in clear.
        self._cipher: SessionCipher | None = None

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
            # The keys die with the session they were derived for.
            self._cipher = None
            try:
                sock.close()
            except OSError:
                pass

    # -- framing -------------------------------------------------------------

    def _send(self, message_type: int, payload: bytes = b"") -> None:
        """Writes one frame: handshake in clear, everything else sealed.

        A non-handshake message with no cipher is refused rather than sent in
        clear. Falling back to plaintext would leak exactly what ADR-0009 exists
        to hide, at the moment the session is already in an unexpected state.
        """
        sock = self._require_socket()
        handshake = is_handshake(message_type)
        if handshake:
            if len(payload) > MAX_PAYLOAD:
                raise GuardProtocolError("payload_too_large", str(len(payload)))
        elif self._cipher is None:
            raise GuardProtocolError("cipher_unavailable", f"0x{message_type:02X}")

        sequence = self._egress.expected
        if handshake:
            body = payload
        else:
            # The header is the associated data, so it must be built before the
            # payload is sealed and must be the very bytes that go on the wire.
            header = HEADER.pack(MAGIC, VERSION, message_type, len(payload) + OVERHEAD, sequence)
            try:
                body = self._cipher.seal(header, payload)  # type: ignore[union-attr]
            except SessionCipherError as exc:
                raise GuardProtocolError("encrypt_failed", exc.reason) from exc

        frame = Frame(message_type=message_type, sequence=sequence, payload=body)
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

        # ADR-0009: past the handshake nothing is readable. A frame that arrives in
        # clear, or that fails its tag, is refused rather than interpreted.
        if not is_handshake(frame.message_type):
            if self._cipher is None:
                raise GuardProtocolError("plaintext_after_handshake", f"0x{frame.message_type:02X}")
            try:
                opened = self._cipher.open(header, frame.payload)
            except SessionCipherError as exc:
                raise GuardProtocolError("decrypt_failed", exc.reason) from exc
            frame = Frame(message_type=frame.message_type, sequence=frame.sequence, payload=opened)

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
        # The phone commits to its own nonce and ephemeral key first, so the
        # runtime's proof is bound to values the phone chose: a replayed proof
        # cannot pass, and the key agreement cannot be steered by a peer on the path.
        client_nonce = create_nonce()
        ephemeral = generate_ephemeral()
        client_ephemeral = public_key_bytes(ephemeral)
        self._send(TYPE_SESSION_HELLO, client_nonce + client_ephemeral)

        capabilities = self._expect(TYPE_CAPABILITIES).payload.decode("utf-8")
        challenge = self._expect(TYPE_AUTH_CHALLENGE).payload
        if len(challenge) != HANDSHAKE_HELLO_LENGTH:
            raise GuardProtocolError("invalid_challenge_length", str(len(challenge)))
        server_nonce = challenge[:NONCE_LENGTH]
        server_ephemeral = challenge[NONCE_LENGTH:]

        # The runtime proves itself before the phone signs anything, and before the
        # phone derives anything. Signing first would mean answering a peer that has
        # shown nothing; deriving first would mean keying off its ephemeral key.
        proof = self._expect(TYPE_SERVER_AUTH_PROOF).payload
        if not self._runtime_proof_valid(client_nonce, server_nonce, client_ephemeral, server_ephemeral, proof):
            raise GuardProtocolError("runtime_proof_rejected")

        binding = compute_binding(client_nonce, server_nonce, client_ephemeral, server_ephemeral)
        try:
            material = derive_session_material(ephemeral, server_ephemeral, binding)
        except SessionCipherError as exc:
            raise GuardProtocolError("invalid_server_ephemeral_key", exc.reason) from exc
        self._cipher = SessionCipher.for_phone(material)

        self._send(
            TYPE_AUTH_RESPONSE,
            self.sign_transcript(client_nonce, server_nonce, client_ephemeral, server_ephemeral),
        )
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

    def sign_transcript(
        self,
        client_nonce: bytes,
        server_nonce: bytes,
        client_ephemeral: bytes,
        server_ephemeral: bytes,
    ) -> bytes:
        """Signs the session transcript as the client.

        Prehashed on purpose: both sides sign a digest they each compute, so
        neither can hand the other arbitrary bytes to put a signature on. The
        digest covers both ephemeral keys, so this one signature also
        authenticates the key agreement.
        """
        digest = compute(ROLE_CLIENT, client_nonce, server_nonce, client_ephemeral, server_ephemeral)
        return self._key.sign(digest, padding.PKCS1v15(), Prehashed(hashes.SHA256()))

    def _runtime_proof_valid(
        self,
        client_nonce: bytes,
        server_nonce: bytes,
        client_ephemeral: bytes,
        server_ephemeral: bytes,
        proof: bytes,
    ) -> bool:
        digest = compute(ROLE_SERVER, client_nonce, server_nonce, client_ephemeral, server_ephemeral)
        try:
            self._runtime_key.verify(proof, digest, padding.PKCS1v15(), Prehashed(hashes.SHA256()))
            return True
        except InvalidSignature:
            return False

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
