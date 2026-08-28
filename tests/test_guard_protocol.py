from nosai.guard.protocol import GuardEndpoint, MessageType, make_heartbeat, make_hello


def test_hello_contract():
    message = make_hello(GuardEndpoint("pc-guard", "PLAY_GUARD", "0.1.0"), "session-1")
    assert message.message_type is MessageType.HELLO
    assert message.session_id == "session-1"
    assert message.payload["role"] == "PLAY_GUARD"


def test_heartbeat_contract():
    message = make_heartbeat("session-1", 7, 1200)
    assert message.message_type is MessageType.HEARTBEAT
    assert message.sequence == 7
    assert message.payload["uptime_ms"] == 1200
