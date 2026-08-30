"""The handshake transcript must match the C# layout byte-for-byte.

The pinned digests below have twins in `tests/NosAi.Runtime.Tests/SessionTranscriptTests.cs`.
A divergence would otherwise surface only as a phone that can no longer
authenticate, with both sides believing they were right.
"""
from __future__ import annotations

import hashlib

import pytest

from nosai.network.session_transcript import (
    EPHEMERAL_KEY_LENGTH,
    LABEL,
    ROLE_BINDING,
    HandshakeRole,
    compute,
    compute_binding,
    create_nonce,
    sign,
    verify,
)
from nosai.phone.guard_client import generate_client_key

# Chosen so the vectors are reproducible by hand: 0..31 and 255..224 for the
# nonces, and a 0x04-prefixed ramp for each ephemeral key. These stand in for
# real P-256 points on purpose — the transcript hashes the encoded bytes and
# never interprets them, so the vector stays independent of key generation.
CLIENT_NONCE = bytes(range(32))
SERVER_NONCE = bytes(255 - i for i in range(32))
CLIENT_EPHEMERAL = bytes([0x04]) + bytes(range(64))
SERVER_EPHEMERAL = bytes([0x04]) + bytes(255 - i for i in range(64))

CLIENT_DIGEST = "C21C431996795F1008869B2F2F404788065FEBB2B4D540EBA6E10586EB81DCCB"
SERVER_DIGEST = "4FA15241CCA7785A61BA9ADA88CD5C6C6C3330BDA4B9C7160D6F50E8F6E59047"
BINDING_DIGEST = "EEA2EFAC25055CB73768C2C38E4150E682441F83A2D9EDF8056FEC37078DD397"


def _digests():
    return (
        compute(HandshakeRole.CLIENT, CLIENT_NONCE, SERVER_NONCE, CLIENT_EPHEMERAL, SERVER_EPHEMERAL),
        compute(HandshakeRole.SERVER, CLIENT_NONCE, SERVER_NONCE, CLIENT_EPHEMERAL, SERVER_EPHEMERAL),
    )


def test_known_inputs_produce_the_pinned_digests():
    client, server = _digests()
    assert client.hex().upper() == CLIENT_DIGEST
    assert server.hex().upper() == SERVER_DIGEST
    binding = compute_binding(CLIENT_NONCE, SERVER_NONCE, CLIENT_EPHEMERAL, SERVER_EPHEMERAL)
    assert binding.hex().upper() == BINDING_DIGEST


def test_the_digest_is_the_documented_buffer():
    buffer = (
        LABEL
        + b"\x00"
        + bytes((HandshakeRole.CLIENT,))
        + b"\x00"
        + CLIENT_NONCE
        + SERVER_NONCE
        + CLIENT_EPHEMERAL
        + SERVER_EPHEMERAL
    )
    client, _ = _digests()
    assert client == hashlib.sha256(buffer).digest()


def test_the_two_roles_never_produce_the_same_digest():
    client, server = _digests()
    assert client != server


def test_the_binding_role_is_not_a_signing_role():
    # A key-derivation input must never collide with something either side signs.
    assert ROLE_BINDING not in (HandshakeRole.CLIENT, HandshakeRole.SERVER)
    binding = compute_binding(CLIENT_NONCE, SERVER_NONCE, CLIENT_EPHEMERAL, SERVER_EPHEMERAL)
    client, server = _digests()
    assert binding not in (client, server)


def test_a_version_two_transcript_does_not_verify_under_version_three():
    # No downgrade: the label and the added ephemeral keys mean a version 2 peer's
    # signature is simply not a version 3 signature.
    legacy_buffer = (
        b"NOSAI-GUARD-HANDSHAKE-V2"
        + b"\x00"
        + bytes((HandshakeRole.CLIENT,))
        + b"\x00"
        + CLIENT_NONCE
        + SERVER_NONCE
    )
    client, _ = _digests()
    assert hashlib.sha256(legacy_buffer).digest() != client


def test_the_ephemeral_keys_change_the_digest():
    # This is what authenticates the key agreement: swap an ephemeral key and the
    # signature that carried it no longer verifies.
    client, _ = _digests()
    other = bytes([0x04]) + bytes(64)
    assert compute(HandshakeRole.CLIENT, CLIENT_NONCE, SERVER_NONCE, other, SERVER_EPHEMERAL) != client
    assert compute(HandshakeRole.CLIENT, CLIENT_NONCE, SERVER_NONCE, CLIENT_EPHEMERAL, other) != client


def test_a_malformed_field_is_refused():
    # A peer that will not commit to a full nonce or a full point cannot be given a
    # session-bound proof, and accepting one would mean signing over material it
    # fully controls.
    with pytest.raises(ValueError):
        compute(HandshakeRole.CLIENT, bytes(31), SERVER_NONCE, CLIENT_EPHEMERAL, SERVER_EPHEMERAL)
    with pytest.raises(ValueError):
        compute(HandshakeRole.CLIENT, CLIENT_NONCE, bytes(33), CLIENT_EPHEMERAL, SERVER_EPHEMERAL)
    with pytest.raises(ValueError):
        compute(HandshakeRole.CLIENT, CLIENT_NONCE, SERVER_NONCE, bytes(64), SERVER_EPHEMERAL)
    with pytest.raises(ValueError):
        compute(HandshakeRole.CLIENT, CLIENT_NONCE, SERVER_NONCE, CLIENT_EPHEMERAL, bytes(EPHEMERAL_KEY_LENGTH + 1))


def test_a_signature_verifies_only_for_its_role():
    key = generate_client_key()
    client = create_nonce()
    server = create_nonce()
    signature = sign(key, HandshakeRole.CLIENT, client, server, CLIENT_EPHEMERAL, SERVER_EPHEMERAL)
    assert verify(key, HandshakeRole.CLIENT, client, server, CLIENT_EPHEMERAL, SERVER_EPHEMERAL, signature)
    assert not verify(key, HandshakeRole.SERVER, client, server, CLIENT_EPHEMERAL, SERVER_EPHEMERAL, signature)


def test_a_signature_does_not_verify_under_swapped_nonces():
    key = generate_client_key()
    client = create_nonce()
    server = create_nonce()
    signature = sign(key, HandshakeRole.CLIENT, client, server, CLIENT_EPHEMERAL, SERVER_EPHEMERAL)
    assert not verify(key, HandshakeRole.CLIENT, server, client, CLIENT_EPHEMERAL, SERVER_EPHEMERAL, signature)


def test_a_signature_does_not_verify_under_a_swapped_ephemeral_key():
    key = generate_client_key()
    client = create_nonce()
    server = create_nonce()
    signature = sign(key, HandshakeRole.CLIENT, client, server, CLIENT_EPHEMERAL, SERVER_EPHEMERAL)
    assert not verify(key, HandshakeRole.CLIENT, client, server, SERVER_EPHEMERAL, CLIENT_EPHEMERAL, signature)
