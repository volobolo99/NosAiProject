"""RSA authentication primitives for the PC↔phone SESSION_AUTH handshake."""

from __future__ import annotations

import base64
import binascii
import hashlib
import secrets
from pathlib import Path
from typing import Optional

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa


class NosAiCryptoAuthManager:
    """Verify a phone Guard AI RSA-2048 signature over a one-shot challenge.

    Private keys are never loaded by this class. The public key is expected to
    live on the dedicated NOSAI-SSD volume.
    """

    CHALLENGE_BYTES = 32

    def __init__(self, public_key_path: str | Path):
        self.public_key_path = Path(public_key_path)
        self._cached_public_key: rsa.RSAPublicKey | None = None
        self._last_generated_challenge: bytes | None = None

    def _load_public_key(self) -> rsa.RSAPublicKey:
        if self._cached_public_key is not None:
            return self._cached_public_key
        if not self.public_key_path.is_file():
            raise FileNotFoundError(
                f"Chiave pubblica di Guard AI assente: {self.public_key_path}"
            )
        key = serialization.load_pem_public_key(self.public_key_path.read_bytes())
        if not isinstance(key, rsa.RSAPublicKey) or key.key_size != 2048:
            raise ValueError("La chiave Guard AI deve essere RSA-2048")
        self._cached_public_key = key
        return key

    def generate_secure_challenge(self) -> str:
        """Generate a 32-byte cryptographic nonce and return its hex encoding."""
        self._last_generated_challenge = secrets.token_bytes(self.CHALLENGE_BYTES)
        return self._last_generated_challenge.hex()

    @staticmethod
    def challenge_audit_digest(challenge_hex: str) -> str:
        """Return SHA-256 of the wire challenge for audit/provenance."""
        challenge = bytes.fromhex(challenge_hex)
        if len(challenge) != NosAiCryptoAuthManager.CHALLENGE_BYTES:
            raise ValueError("La challenge deve essere lunga 32 byte")
        return hashlib.sha256(challenge).hexdigest()

    def verify_phone_signature(
        self, b64_signature: str, original_challenge_hex: Optional[str] = None
    ) -> bool:
        """Verify RSA PKCS#1 v1.5 + SHA-256 and consume the challenge exactly once.

        ``original_challenge_hex`` may only confirm the challenge that is
        currently pending. It can never designate a different or an already
        consumed challenge, otherwise a captured signature could be replayed
        indefinitely by supplying its challenge explicitly.
        """
        # Consume first: SUCCESS, malformed input and FAIL must all invalidate it.
        challenge = self._last_generated_challenge
        self._last_generated_challenge = None
        if challenge is None or len(challenge) != self.CHALLENGE_BYTES:
            return False

        if original_challenge_hex is not None:
            try:
                supplied = bytes.fromhex(original_challenge_hex)
            except (ValueError, TypeError):
                return False
            if not secrets.compare_digest(supplied, challenge):
                return False

        try:
            signature = base64.b64decode(b64_signature.encode("ascii"), validate=True)
            self._load_public_key().verify(
                signature,
                challenge.hex().encode("ascii"),
                padding.PKCS1v15(),
                hashes.SHA256(),
            )
            return True
        except (ValueError, TypeError, binascii.Error, UnicodeError):
            return False
        except Exception:
            # Cryptographic verification is fail-closed at this boundary.
            return False
