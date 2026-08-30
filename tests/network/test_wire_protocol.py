import pytest
from nosai.network.wire_protocol import Frame, SequenceGuard, decode


def test_frame_round_trip():
    raw = Frame(0x10, 1, b'hello').encode()
    assert decode(raw) == Frame(0x10, 1, b'hello')


def test_invalid_magic():
    with pytest.raises(ValueError):
        decode(b'XXXX\x02\x10\x00\x00\x00\x00\x00\x01')


def test_decode_rejects_wire_version_one():
    # Version 1 cannot prove the runtime to the phone. Accepting it would reopen
    # the hole version 2 closed.
    v1 = b"NOSA" + b"\x01" + b"\x11" + b"\x00\x00" + b"\x00\x00\x00\x01"
    with pytest.raises(ValueError):
        decode(v1)


def test_sequence_guard_is_strictly_monotonic():
    guard = SequenceGuard(1)
    assert guard.accept(1)
    assert not guard.accept(1)
    assert guard.accept(2)
