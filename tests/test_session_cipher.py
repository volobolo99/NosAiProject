"""Session payload encryption (ADR-0009), pinned against the C# implementation.

The vectors here have twins in `tests/NosAi.Runtime.Tests/SessionCipherTests.cs`.
Both languages agree on the key schedule and on the exact bytes of a sealed
frame, so a divergence fails a test instead of appearing as a phone that
authenticates and then cannot read anything.
"""
from __future__ import annotations

import pytest
from cryptography.hazmat.primitives.asymmetric import ec

from nosai.network import session_cipher as sc
from nosai.network.session_transcript import compute_binding
from nosai.network.wire_protocol import (
    TYPE_AUTH_RESULT,
    TYPE_CAPABILITIES,
    TYPE_DISCONNECT,
    TYPE_HEARTBEAT,
    TYPE_SESSION_HELLO,
    TYPE_TELEMETRY_SNAPSHOT,
    is_handshake,
)

# Fixed scalars so the agreement is a known answer rather than a fresh random one.
# These are test vectors, not credentials: they protect nothing and are pinned in
# both languages precisely so a mismatch is visible.
CLIENT_SCALAR = 0x0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20
SERVER_SCALAR = 0x202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F

CLIENT_NONCE = bytes(range(32))
SERVER_NONCE = bytes(255 - i for i in range(32))

CLIENT_PUBLIC = "04515C3D6EB9E396B904D3FECA7F54FDCD0CC1E997BF375DCA515AD0A6C3B4035F4536BE3A50F318FBF9A5475902A221502BEF0D57E08C53B2CC0A56F17D9F9354"
SERVER_PUBLIC = "04C6559D416DFB56AF714F146D917C24ABF818B2FB121604129649848230A2D258B2A6D82DC6C6734CF092FFAA9FC012F10F7008D3952A08D5797E85FEABA5D977"
SESSION_MATERIAL = (
    "E60A5234E58D2840E2A2B5F14A68FCB4EE8BFD10B40E4B331916B80314308ADA"
    "8C86BE6149B84DB5EAB8C5C38C8C5C36DC3261EB26CAE85E63872F392F18384B"
)

# A TelemetrySnapshot frame: NOSA, version 3, type 0x11, payload 0x2A, sequence 7.
GOLDEN_HEADER = "4E4F53410311002A00000007"
GOLDEN_PLAINTEXT = b"gate1-snapshot"
GOLDEN_PAYLOAD = "000000000000000000000000803FC5ED76FBFF5E0AEB85DCAE312A4ACD2BFDFE1D57628D1C92FBC16466"


def _fixed_keys():
    return (
        ec.derive_private_key(CLIENT_SCALAR, sc.CURVE),
        ec.derive_private_key(SERVER_SCALAR, sc.CURVE),
    )


def _fixed_material():
    client_key, server_key = _fixed_keys()
    client_pub = sc.public_key_bytes(client_key)
    server_pub = sc.public_key_bytes(server_key)
    binding = compute_binding(CLIENT_NONCE, SERVER_NONCE, client_pub, server_pub)
    return sc.derive_session_material(client_key, server_pub, binding)


def test_the_fixed_keys_encode_to_the_pinned_public_points():
    client_key, server_key = _fixed_keys()
    assert sc.public_key_bytes(client_key).hex().upper() == CLIENT_PUBLIC
    assert sc.public_key_bytes(server_key).hex().upper() == SERVER_PUBLIC


def test_both_sides_derive_the_same_pinned_material():
    client_key, server_key = _fixed_keys()
    client_pub = sc.public_key_bytes(client_key)
    server_pub = sc.public_key_bytes(server_key)
    binding = compute_binding(CLIENT_NONCE, SERVER_NONCE, client_pub, server_pub)

    from_client = sc.derive_session_material(client_key, server_pub, binding)
    from_server = sc.derive_session_material(server_key, client_pub, binding)

    assert from_client == from_server
    assert from_client.hex().upper() == SESSION_MATERIAL
    assert len(from_client) == sc.SESSION_MATERIAL_LENGTH


def test_a_different_binding_derives_different_keys():
    # The binding is the HKDF salt, so a peer that saw a different handshake ends
    # up unable to decrypt rather than quietly agreeing on a key.
    client_key, server_key = _fixed_keys()
    server_pub = sc.public_key_bytes(server_key)
    good = compute_binding(CLIENT_NONCE, SERVER_NONCE, sc.public_key_bytes(client_key), server_pub)
    other = compute_binding(SERVER_NONCE, CLIENT_NONCE, sc.public_key_bytes(client_key), server_pub)
    assert sc.derive_session_material(client_key, server_pub, good) != sc.derive_session_material(
        client_key, server_pub, other
    )


def test_the_golden_frame_is_byte_for_byte_reproducible():
    phone = sc.SessionCipher.for_phone(_fixed_material())
    header = bytes.fromhex(GOLDEN_HEADER)
    assert phone.seal(header, GOLDEN_PLAINTEXT).hex().upper() == GOLDEN_PAYLOAD


def test_the_runtime_opens_what_the_phone_sealed():
    material = _fixed_material()
    phone = sc.SessionCipher.for_phone(material)
    runtime = sc.SessionCipher.for_runtime(material)
    header = bytes.fromhex(GOLDEN_HEADER)
    assert runtime.open(header, phone.seal(header, GOLDEN_PLAINTEXT)) == GOLDEN_PLAINTEXT


def test_the_directions_do_not_share_a_key():
    # With one shared key a frame captured in one direction would decrypt when
    # replayed down the other.
    material = _fixed_material()
    phone = sc.SessionCipher.for_phone(material)
    other_phone = sc.SessionCipher.for_phone(material)
    header = bytes.fromhex(GOLDEN_HEADER)
    sealed = phone.seal(header, GOLDEN_PLAINTEXT)
    with pytest.raises(sc.SessionCipherError) as excinfo:
        other_phone.open(header, sealed)
    assert excinfo.value.reason == "authentication_failed"


def test_a_tampered_header_fails_the_tag():
    # The header is readable but authenticated: rewriting the type or the sequence
    # number must not go unnoticed.
    material = _fixed_material()
    phone = sc.SessionCipher.for_phone(material)
    runtime = sc.SessionCipher.for_runtime(material)
    header = bytearray(bytes.fromhex(GOLDEN_HEADER))
    sealed = phone.seal(bytes(header), GOLDEN_PLAINTEXT)
    header[5] = TYPE_HEARTBEAT
    with pytest.raises(sc.SessionCipherError) as excinfo:
        runtime.open(bytes(header), sealed)
    assert excinfo.value.reason == "authentication_failed"


def test_a_tampered_ciphertext_fails_the_tag():
    material = _fixed_material()
    phone = sc.SessionCipher.for_phone(material)
    runtime = sc.SessionCipher.for_runtime(material)
    header = bytes.fromhex(GOLDEN_HEADER)
    sealed = bytearray(phone.seal(header, GOLDEN_PLAINTEXT))
    sealed[sc.NONCE_LENGTH] ^= 0x01
    with pytest.raises(sc.SessionCipherError) as excinfo:
        runtime.open(header, bytes(sealed))
    assert excinfo.value.reason == "authentication_failed"


def test_the_nonce_advances_and_is_never_reused():
    material = _fixed_material()
    phone = sc.SessionCipher.for_phone(material)
    header = bytes.fromhex(GOLDEN_HEADER)
    first = phone.seal(header, GOLDEN_PLAINTEXT)
    second = phone.seal(header, GOLDEN_PLAINTEXT)
    assert first[: sc.NONCE_LENGTH] != second[: sc.NONCE_LENGTH]
    assert second[: sc.NONCE_LENGTH] == b"\x00" * 4 + (1).to_bytes(8, "big")
    # Same key, same plaintext, different nonce: the ciphertext must differ too.
    assert first != second


def test_an_out_of_order_nonce_is_refused():
    material = _fixed_material()
    phone = sc.SessionCipher.for_phone(material)
    runtime = sc.SessionCipher.for_runtime(material)
    header = bytes.fromhex(GOLDEN_HEADER)
    phone.seal(header, GOLDEN_PLAINTEXT)
    second = phone.seal(header, GOLDEN_PLAINTEXT)
    # The receiver is still expecting counter 0, so counter 1 is refused before
    # the tag is even considered.
    with pytest.raises(sc.SessionCipherError) as excinfo:
        runtime.open(header, second)
    assert excinfo.value.reason == "nonce_out_of_order"


def test_a_short_payload_is_refused():
    phone = sc.SessionCipher.for_phone(_fixed_material())
    with pytest.raises(sc.SessionCipherError) as excinfo:
        phone.open(bytes.fromhex(GOLDEN_HEADER), bytes(sc.OVERHEAD - 1))
    assert excinfo.value.reason == "encrypted_payload_too_short"


def test_an_invalid_peer_point_is_refused():
    # An unchecked point would let a peer steer the agreement.
    client_key, _ = _fixed_keys()
    binding = bytes(32)
    with pytest.raises(sc.SessionCipherError):
        sc.derive_session_material(client_key, bytes([0x04]) + bytes(64), binding)
    with pytest.raises(sc.SessionCipherError):
        sc.derive_session_material(client_key, bytes(65), binding)


def test_only_handshake_messages_may_travel_in_clear():
    for message_type in (TYPE_SESSION_HELLO, TYPE_CAPABILITIES, TYPE_AUTH_RESULT):
        assert is_handshake(message_type)
    for message_type in (TYPE_HEARTBEAT, TYPE_TELEMETRY_SNAPSHOT, TYPE_DISCONNECT):
        assert not is_handshake(message_type)
