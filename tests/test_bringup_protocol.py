import pytest

from nosai.bringup.protocol import Message, PROTOCOL_VERSION, capabilities, heartbeat, hello


def test_message_round_trip():
    original = hello("abc123", 7, "guard_ai")
    decoded = Message.decode(original.encode())
    assert decoded == original


def test_protocol_version_is_enforced():
    with pytest.raises(ValueError):
        Message.decode(b'{"protocol":"0.9","type":"HELLO","session_id":"x","seq":1,"payload":{}}\n')


def test_message_contracts():
    assert capabilities("s", 2, ["heartbeat"]).payload == {"capabilities": ["heartbeat"]}
    assert heartbeat("s", 3).type == "HEARTBEAT"
    # Bumped to 1.1 by 16bed70, which made the nonce mandatory in the
    # message envelope: an intentional, incompatible protocol change.
    assert PROTOCOL_VERSION == "1.1"
