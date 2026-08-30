"""What each side signs during the Gate 1 handshake (wire version 3).

Must match `SessionTranscript` in `src/NosAi.Protocol/SessionTranscript.cs`
byte-for-byte: a Python client that hashes a different buffer will be refused
by the runtime, and a C# client that hashes a different buffer will be refused
by a Python test double. The layout is therefore pinned by tests, not restated
in comments.

The digest is SHA-256 of:

    label || 0x00 || role || 0x00 || client_nonce(32) || server_nonce(32)
          || client_ephemeral(65) || server_ephemeral(65)

Signatures are PKCS#1 v1.5 over that pre-hashed digest (RSA SignHash), not over
the raw nonces. Signing the raw challenge would put the phone back in the role
of a signing oracle, which is the hole version 2 exists to close.

Version 3 (ADR-0009) adds both ephemeral P-256 public keys to the material. That
is what authenticates the key agreement the session payload is encrypted under:
substituting an ephemeral key invalidates the signature that carried it, so no
second handshake and no second trust root are needed.
"""
from __future__ import annotations

import hashlib
import os

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.hazmat.primitives.asymmetric.utils import Prehashed

LABEL = b"NOSAI-GUARD-HANDSHAKE-V3"
NONCE_LENGTH = 32

#: Length of an ephemeral P-256 public key on the wire: uncompressed X9.62
#: point, ``0x04 || X(32) || Y(32)``. Matches SessionTranscript.EphemeralKeyLength.
EPHEMERAL_KEY_LENGTH = 65


class HandshakeRole:
    """Matches `NosAi.Runtime.Gate1.HandshakeRole`."""

    SERVER = 0x01
    CLIENT = 0x02


ROLE_SERVER = HandshakeRole.SERVER
ROLE_CLIENT = HandshakeRole.CLIENT

#: Role byte used when deriving keys rather than signing. Not a valid signing
#: role, deliberately: a key-derivation input can then never collide with a
#: digest either side would put a signature on.
ROLE_BINDING = 0x00


def create_nonce() -> bytes:
    return os.urandom(NONCE_LENGTH)


def _digest(
    role: int,
    client_nonce: bytes,
    server_nonce: bytes,
    client_ephemeral: bytes,
    server_ephemeral: bytes,
) -> bytes:
    if len(client_nonce) != NONCE_LENGTH:
        raise ValueError(f"client nonce must be {NONCE_LENGTH} bytes")
    if len(server_nonce) != NONCE_LENGTH:
        raise ValueError(f"server nonce must be {NONCE_LENGTH} bytes")
    if len(client_ephemeral) != EPHEMERAL_KEY_LENGTH:
        raise ValueError(f"client ephemeral key must be {EPHEMERAL_KEY_LENGTH} bytes")
    if len(server_ephemeral) != EPHEMERAL_KEY_LENGTH:
        raise ValueError(f"server ephemeral key must be {EPHEMERAL_KEY_LENGTH} bytes")
    buffer = (
        LABEL
        + b"\x00"
        + bytes((role,))
        + b"\x00"
        + client_nonce
        + server_nonce
        + client_ephemeral
        + server_ephemeral
    )
    return hashlib.sha256(buffer).digest()


def compute(
    role: int,
    client_nonce: bytes,
    server_nonce: bytes,
    client_ephemeral: bytes,
    server_ephemeral: bytes,
) -> bytes:
    """The digest the given role signs for this handshake."""
    if role not in (ROLE_SERVER, ROLE_CLIENT):
        raise ValueError("unknown handshake role")
    return _digest(role, client_nonce, server_nonce, client_ephemeral, server_ephemeral)


def compute_binding(
    client_nonce: bytes,
    server_nonce: bytes,
    client_ephemeral: bytes,
    server_ephemeral: bytes,
) -> bytes:
    """The HKDF salt that ties the session keys to this exact handshake."""
    return _digest(ROLE_BINDING, client_nonce, server_nonce, client_ephemeral, server_ephemeral)


def sign(
    key: rsa.RSAPrivateKey,
    role: int,
    client_nonce: bytes,
    server_nonce: bytes,
    client_ephemeral: bytes,
    server_ephemeral: bytes,
) -> bytes:
    digest = compute(role, client_nonce, server_nonce, client_ephemeral, server_ephemeral)
    return key.sign(digest, padding.PKCS1v15(), Prehashed(hashes.SHA256()))


def verify(
    key: rsa.RSAPublicKey | rsa.RSAPrivateKey,
    role: int,
    client_nonce: bytes,
    server_nonce: bytes,
    client_ephemeral: bytes,
    server_ephemeral: bytes,
    signature: bytes,
) -> bool:
    digest = compute(role, client_nonce, server_nonce, client_ephemeral, server_ephemeral)
    public = key if isinstance(key, rsa.RSAPublicKey) else key.public_key()
    try:
        public.verify(signature, digest, padding.PKCS1v15(), Prehashed(hashes.SHA256()))
    except Exception:
        return False
    return True


__all__ = [
    "EPHEMERAL_KEY_LENGTH",
    "LABEL",
    "NONCE_LENGTH",
    "HandshakeRole",
    "ROLE_BINDING",
    "ROLE_CLIENT",
    "ROLE_SERVER",
    "compute",
    "compute_binding",
    "create_nonce",
    "sign",
    "verify",
]
