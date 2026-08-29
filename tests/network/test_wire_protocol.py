import pytest
from nosai.network.wire_protocol import Frame, SequenceGuard, decode


def test_frame_round_trip():
    raw = Frame(0x10, 1, b'hello').encode()
    assert decode(raw) == Frame(0x10, 1, b'hello')


def test_invalid_magic():
    with pytest.raises(ValueError):
        decode(b'XXXX\x01\x10\x00\x00\x00\x00\x00\x01')


def test_sequence_guard_is_strictly_monotonic():
    guard = SequenceGuard(1)
    assert guard.accept(1)
    assert not guard.accept(1)
    assert guard.accept(2)
