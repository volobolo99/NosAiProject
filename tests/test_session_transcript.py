"""The handshake transcript must match the C# layout byte-for-byte."""
from __future__ import annotations

import hashlib

from nosai.network.session_transcript import (
    LABEL,
    HandshakeRole,
    compute,
    create_nonce,
    sign,
    verify,
)
from nosai.phone.guard_client import generate_client_key


def test_known_nonces_produce_a_pinned_digest():
    client = bytes(range(32))
    server = bytes(range(32, 64))
    buffer = LABEL + b"\x00" + bytes((HandshakeRole.CLIENT,)) + b"\x00" + client + server
    assert compute(HandshakeRole.CLIENT, client, server) == hashlib.sha256(buffer).digest()
    assert compute(HandshakeRole.SERVER, client, server) != compute(HandshakeRole.CLIENT, client, server)


def test_a_signature_verifies_only_for_its_role():
    key = generate_client_key()
    client = create_nonce()
    server = create_nonce()
    signature = sign(key, HandshakeRole.CLIENT, client, server)
    assert verify(key, HandshakeRole.CLIENT, client, server, signature)
    assert not verify(key, HandshakeRole.SERVER, client, server, signature)


def test_a_signature_does_not_verify_under_swapped_nonces():
    key = generate_client_key()
    client = create_nonce()
    server = create_nonce()
    signature = sign(key, HandshakeRole.CLIENT, client, server)
    assert not verify(key, HandshakeRole.CLIENT, server, client, signature)
