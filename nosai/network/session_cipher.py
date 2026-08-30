"""Authenticated encryption of the Gate 1 session payload (ADR-0009).

Python counterpart of `src/NosAi.Protocol/SessionCipher.cs`. Every constant and
every byte of layout below has a pinned twin on the C# side; a divergence would
otherwise surface as a phone that connects, authenticates, and then cannot read
a single frame.

Key schedule::

    Z        = ECDH(P-256, own ephemeral private, peer ephemeral public)
    ikm      = SHA-256(Z)
    binding  = session_transcript.compute_binding(...)
    keys(64) = HKDF-SHA256(ikm, salt=binding, info=b"NOSAI-GUARD-SESSION-V3")
    c2s      = keys[0:32]     client -> server
    s2c      = keys[32:64]    server -> client

Frame layout after the handshake::

    header(12, clear, authenticated) || nonce(12) || ciphertext || tag(16)

The header stays readable because the stream cannot be framed otherwise, and is
passed as associated data so the type, length and sequence number are still
authenticated. Keys are directional: with one shared key a frame captured in one
direction would decrypt when replayed down the other.
"""
from __future__ import annotations

import hashlib
import struct
import threading

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat

from nosai.network.session_transcript import EPHEMERAL_KEY_LENGTH

#: HKDF info string. Shared verbatim with EphemeralKeyExchange.KeyScheduleInfo.
KEY_SCHEDULE_INFO = b"NOSAI-GUARD-SESSION-V3"

KEY_LENGTH = 32
NONCE_LENGTH = 12
TAG_LENGTH = 16

#: Bytes a sealed payload adds to the plaintext.
OVERHEAD = NONCE_LENGTH + TAG_LENGTH

#: Two directional keys.
SESSION_MATERIAL_LENGTH = KEY_LENGTH * 2

CURVE = ec.SECP256R1()

_UNCOMPRESSED_POINT_PREFIX = 0x04


class SessionCipherError(RuntimeError):
    """A frame could not be sealed or opened. `reason` is a stable identifier."""

    def __init__(self, reason: str, detail: str | None = None):
        super().__init__(f"{reason}: {detail}" if detail else reason)
        self.reason = reason
        self.detail = detail


def generate_ephemeral() -> ec.EllipticCurvePrivateKey:
    """A per-session P-256 key pair. Never persisted: that is the point."""
    return ec.generate_private_key(CURVE)


def public_key_bytes(private_key: ec.EllipticCurvePrivateKey) -> bytes:
    """The ephemeral public key as it appears on the wire (uncompressed X9.62)."""
    encoded = private_key.public_key().public_bytes(Encoding.X962, PublicFormat.UncompressedPoint)
    if len(encoded) != EPHEMERAL_KEY_LENGTH:
        raise SessionCipherError("invalid_ephemeral_encoding", str(len(encoded)))
    return encoded


def load_peer_public_key(peer: bytes) -> ec.EllipticCurvePublicKey:
    """Parse and validate a peer ephemeral key.

    `from_encoded_point` rejects a point that is not on the curve. Skipping that
    check would let a peer pick a value that steers the agreement.
    """
    if len(peer) != EPHEMERAL_KEY_LENGTH or peer[0] != _UNCOMPRESSED_POINT_PREFIX:
        raise SessionCipherError("invalid_peer_ephemeral_key", str(len(peer)))
    try:
        return ec.EllipticCurvePublicKey.from_encoded_point(CURVE, peer)
    except ValueError as exc:
        raise SessionCipherError("invalid_peer_ephemeral_key", str(exc)) from exc


def derive_session_material(
    private_key: ec.EllipticCurvePrivateKey,
    peer_public_key: bytes,
    binding: bytes,
) -> bytes:
    """The 64 bytes of directional key material for this handshake.

    The binding is the HKDF salt, so a peer that saw different nonces or a
    different ephemeral key derives different keys and simply cannot decrypt: a
    mismatch shows up as a failed tag, never as plausible-looking wrong data.
    """
    peer = load_peer_public_key(peer_public_key)
    shared = private_key.exchange(ec.ECDH(), peer)
    # SHA-256(Z) matches ECDiffieHellman.DeriveKeyFromHash with no prepend or
    # append on the C# side. Pinned by tests in both languages.
    ikm = hashlib.sha256(shared).digest()
    return HKDF(
        algorithm=hashes.SHA256(),
        length=SESSION_MATERIAL_LENGTH,
        salt=binding,
        info=KEY_SCHEDULE_INFO,
    ).derive(ikm)


def _nonce(counter: int) -> bytes:
    """Four zero bytes then a big-endian 64-bit counter. Matches the C# layout."""
    return b"\x00\x00\x00\x00" + struct.pack(">Q", counter)


class SessionCipher:
    """Seals and opens Gate 1 session frames.

    The nonce is a per-direction counter that never wraps: at exhaustion the
    sender refuses to encrypt rather than repeat one, because a repeated nonce
    under GCM forfeits both confidentiality and integrity. The receiver requires
    the nonce to be exactly the one it expects, which leaves the peer no freedom
    to choose it while keeping a captured frame decryptable on its own.
    """

    _MAX_COUNTER = 0xFFFFFFFFFFFFFFFF

    def __init__(self, send_key: bytes, receive_key: bytes):
        if len(send_key) != KEY_LENGTH or len(receive_key) != KEY_LENGTH:
            raise SessionCipherError("invalid_key_length")
        self._send = AESGCM(send_key)
        self._receive = AESGCM(receive_key)
        self._send_counter = 0
        self._receive_counter = 0
        self._lock = threading.Lock()

    @classmethod
    def for_phone(cls, material: bytes) -> SessionCipher:
        """The phone's half: sends client->server, receives server->client."""
        cls._require_material(material)
        return cls(material[:KEY_LENGTH], material[KEY_LENGTH:])

    @classmethod
    def for_runtime(cls, material: bytes) -> SessionCipher:
        """The runtime's half: sends server->client, receives client->server."""
        cls._require_material(material)
        return cls(material[KEY_LENGTH:], material[:KEY_LENGTH])

    @staticmethod
    def _require_material(material: bytes) -> None:
        if len(material) != SESSION_MATERIAL_LENGTH:
            raise SessionCipherError("invalid_session_material", str(len(material)))

    def seal(self, header: bytes, plaintext: bytes) -> bytes:
        """Returns nonce(12) || ciphertext || tag(16) for the given header."""
        with self._lock:
            if self._send_counter >= self._MAX_COUNTER:
                raise SessionCipherError("nonce_space_exhausted")
            nonce = _nonce(self._send_counter)
            sealed = self._send.encrypt(nonce, plaintext, header)
            self._send_counter += 1
        return nonce + sealed

    def open(self, header: bytes, payload: bytes) -> bytes:
        """Opens a sealed payload against the header it arrived with."""
        if len(payload) < OVERHEAD:
            raise SessionCipherError("encrypted_payload_too_short", str(len(payload)))
        with self._lock:
            expected = _nonce(self._receive_counter)
            nonce = payload[:NONCE_LENGTH]
            if nonce != expected:
                raise SessionCipherError("nonce_out_of_order")
            try:
                plaintext = self._receive.decrypt(nonce, payload[NONCE_LENGTH:], header)
            except InvalidTag as exc:
                # Wrong key, tampered ciphertext, or a header that does not match
                # what the sender authenticated. All the same answer.
                raise SessionCipherError("authentication_failed") from exc
            if self._receive_counter >= self._MAX_COUNTER:
                raise SessionCipherError("nonce_space_exhausted")
            self._receive_counter += 1
        return plaintext


__all__ = [
    "CURVE",
    "KEY_LENGTH",
    "KEY_SCHEDULE_INFO",
    "NONCE_LENGTH",
    "OVERHEAD",
    "SESSION_MATERIAL_LENGTH",
    "TAG_LENGTH",
    "SessionCipher",
    "SessionCipherError",
    "derive_session_material",
    "generate_ephemeral",
    "load_peer_public_key",
    "public_key_bytes",
]
