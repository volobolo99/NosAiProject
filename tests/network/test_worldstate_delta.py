from nosai.network.wire_protocol import apply_delta, encode_delta


def test_worldstate_delta_roundtrip():
    previous = {"hp": 100, "mp": 50, "zone": "A"}
    current = {"hp": 80, "zone": "B", "target": "mob-1"}
    delta = encode_delta(previous, current)
    assert apply_delta(previous, delta) == current


def test_worldstate_delta_is_deterministic():
    previous = {"a": 1}
    assert encode_delta(previous, {"b": 2, "a": 3}) == encode_delta(previous, {"a": 3, "b": 2})
