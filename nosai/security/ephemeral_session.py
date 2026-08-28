"""Gestione di una sessione cifrata con chiavi effimere.

Il modulo implementa il concetto architetturale della specifica v1.9:
chiave statica del server + chiave effimera del client, derivazione di una
chiave di sessione e cifratura AEAD. Non pretende di essere un'implementazione
completa del Noise Protocol Framework; per il protocollo Noise completo deve
essere usata una libreria Noise validata e testata.
"""
from __future__ import annotations

import os
from dataclasses import dataclass
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey, X25519PublicKey
from cryptography.hazmat.primitives.ciphers.aead import ChaCha20Poly1305
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

PROLOGO = b"NOS_AI_PROTOCOL_V1"


def _kdf(shared_secret: bytes, prologue: bytes = PROLOGO) -> bytes:
    return HKDF(algorithm=hashes.SHA256(), length=32, salt=None, info=prologue).derive(shared_secret)


@dataclass(frozen=True)
class EphemeralIdentity:
    private_key: X25519PrivateKey

    @classmethod
    def generate(cls) -> "EphemeralIdentity":
        return cls(X25519PrivateKey.generate())

    @property
    def public_bytes(self) -> bytes:
        return self.private_key.public_key().public_bytes(
            serialization.Encoding.Raw, serialization.PublicFormat.Raw
        )

    def destroy_reference(self) -> None:
        """Rimuove il riferimento Python alla chiave; non garantisce zeroizzazione RAM."""
        object.__setattr__(self, "private_key", None)  # type: ignore[arg-type]


class EphemeralSession:
    """Sessione AEAD derivata da X25519 + HKDF.

    Il contatore impedisce il riutilizzo del nonce nella stessa direzione.
    L'autenticazione dell'identità del server/client deve essere effettuata
    prima di accettare la sessione tramite la policy del progetto.
    """

    def __init__(self, key: bytes) -> None:
        if len(key) != 32:
            raise ValueError("la chiave di sessione deve essere lunga 32 byte")
        self._aead = ChaCha20Poly1305(key)
        self._counter = 0

    @classmethod
    def from_x25519(cls, private_key: X25519PrivateKey, peer_public_bytes: bytes) -> "EphemeralSession":
        if len(peer_public_bytes) != 32:
            raise ValueError("chiave pubblica X25519 non valida")
        peer = X25519PublicKey.from_public_bytes(peer_public_bytes)
        return cls(_kdf(private_key.exchange(peer)))

    def _nonce(self) -> bytes:
        if self._counter >= (1 << 64):
            raise OverflowError("spazio nonce esaurito: creare una nuova sessione")
        nonce = b"\x00\x00\x00\x00" + self._counter.to_bytes(8, "big")
        self._counter += 1
        return nonce

    def encrypt(self, payload: bytes, associated_data: bytes = b"") -> bytes:
        nonce = self._nonce()
        return nonce + self._aead.encrypt(nonce, payload, associated_data)

    def decrypt(self, packet: bytes, associated_data: bytes = b"") -> bytes:
        if len(packet) < 12 + 16:
            raise ValueError("pacchetto cifrato troppo corto")
        nonce, ciphertext = packet[:12], packet[12:]
        return self._aead.decrypt(nonce, ciphertext, associated_data)


def generate_static_x25519_keypair() -> tuple[bytes, bytes]:
    """Genera una coppia statica nel formato raw.

    La chiave privata non deve essere salvata nel repository. Il progetto deve
    usare un gestore segreti o un file locale escluso dal controllo versione.
    """
    private = X25519PrivateKey.generate()
    return (
        private.private_bytes_raw(),
        private.public_key().public_bytes_raw(),
    )


def generate_ephemeral_public_key() -> tuple[X25519PrivateKey, bytes]:
    private = X25519PrivateKey.generate()
    return private, private.public_key().public_bytes_raw()
