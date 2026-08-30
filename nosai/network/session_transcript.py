"""What each side signs during the Gate 1 handshake (wire version 2).

Must match `SessionTranscript` in `src/NosAi.Protocol/SessionTranscript.cs`
byte-for-byte: a Python client that hashes a different buffer will be refused
by the runtime, and a C# client that hashes a different buffer will be refused
by a Python test double. The layout is therefore pinned by tests, not restated
in comments.

The digest is SHA-256 of:

    label || 0x00 || role || 0x00 || client_nonce(32) || server_nonce(32)

Signatures are PKCS#1 v1.5 over that pre-hashed digest (RSA SignHash), not over
the raw nonces. Signing the raw challenge would put the phone back in the role
of a signing oracle, which is the hole version 2 exists to close.
"""
from __future__ import annotations

import hashlib
import os

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.hazmat.primitives.asymmetric.utils import Prehashed

LABEL = b"NOSAI-GUARD-HANDSHAKE-V2"
NONCE_LENGTH = 32


class HandshakeRole:
    """Matches `NosAi.Runtime.Gate1.HandshakeRole`."""

    SERVER = 0x01
    CLIENT = 0x02


ROLE_SERVER = HandshakeRole.SERVER
ROLE_CLIENT = HandshakeRole.CLIENT


def create_nonce() -> bytes:
    return os.urandom(NONCE_LENGTH)


def compute(role: int, client_nonce: bytes, server_nonce: bytes) -> bytes:
    if role not in (ROLE_SERVER, ROLE_CLIENT):
        raise ValueError("unknown handshake role")
    if len(client_nonce) != NONCE_LENGTH:
        raise ValueError(f"client nonce must be {NONCE_LENGTH} bytes")
    if len(server_nonce) != NONCE_LENGTH:
        raise ValueError(f"server nonce must be {NONCE_LENGTH} bytes")
    buffer = LABEL + b"\x00" + bytes((role,)) + b"\x00" + client_nonce + server_nonce
    return hashlib.sha256(buffer).digest()


def sign(key: rsa.RSAPrivateKey, role: int, client_nonce: bytes, server_nonce: bytes) -> bytes:
    digest = compute(role, client_nonce, server_nonce)
    return key.sign(digest, padding.PKCS1v15(), Prehashed(hashes.SHA256()))


def verify(key: rsa.RSAPublicKey | rsa.RSAPrivateKey, role: int, client_nonce: bytes, server_nonce: bytes, signature: bytes) -> bool:
    digest = compute(role, client_nonce, server_nonce)
    public = key if isinstance(key, rsa.RSAPublicKey) else key.public_key()
    try:
        public.verify(signature, digest, padding.PKCS1v15(), Prehashed(hashes.SHA256()))
    except Exception:
        return False
    return True


__all__ = [
    "LABEL",
    "NONCE_LENGTH",
    "HandshakeRole",
    "ROLE_CLIENT",
    "ROLE_SERVER",
    "compute",
    "create_nonce",
    "sign",
    "verify",
]
