"""RSA-2048/SHA-256 SESSION_AUTH primitives for the PC↔Phone onboarding contract."""
from __future__ import annotations

import base64
import hashlib
import secrets
from dataclasses import dataclass

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

CHALLENGE_BYTES = 32
NONCE_BYTES = 32


def new_challenge() -> bytes:
    return secrets.token_bytes(CHALLENGE_BYTES)


def generate_keypair() -> tuple[bytes, bytes]:
    private = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    return (
        private.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()),
        private.public_key().public_bytes(serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo),
    )


def verify_session_auth(public_key_pem: bytes, challenge: bytes, signature: bytes) -> bool:
    key = serialization.load_pem_public_key(public_key_pem)
    if not isinstance(key, rsa.RSAPublicKey) or key.key_size != 2048:
        return False
    try:
        key.verify(signature, challenge, padding.PKCS1v15(), hashes.SHA256())
    except (InvalidSignature, ValueError, TypeError):
        return False
    return True


def digest_challenge(challenge: bytes) -> str:
    return hashlib.sha256(challenge).hexdigest()


@dataclass(frozen=True)
class SessionAuthResult:
    authenticated: bool
    challenge_digest: str
    reason: str


def authenticate(public_key_pem: bytes, challenge: bytes, signature: bytes) -> SessionAuthResult:
    ok = verify_session_auth(public_key_pem, challenge, signature)
    return SessionAuthResult(ok, digest_challenge(challenge), "ok" if ok else "invalid_signature")
