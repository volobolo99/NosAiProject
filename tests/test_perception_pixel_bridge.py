from nosai.core.data_classification import DataSource, unknown_published_value_errors
from nosai.perception.pixel_bridge import (
    PIXEL_BRIDGE_ANCHOR_RGB,
    PIXEL_BRIDGE_BLOCK_COUNT,
    decode_pixel_bridge,
    encode_pixel_bridge_packet,
    iter_packet_failure_reasons,
)


def _valid_packet(**overrides):
    values = {
        "hp": 7305,
        "mp": 1420,
        "target_hp": 64,
        "target_level": 18,
        "distance": 12.5,
        "in_combat": True,
        "target_is_enemy": True,
        "target_is_dead": False,
    }
    values.update(overrides)
    return encode_pixel_bridge_packet(**values)


def test_valid_packet_decodes_derived_vitals():
    observation = decode_pixel_bridge(_valid_packet())
    assert observation.hp.source is DataSource.DERIVED
    assert observation.mp.source is DataSource.DERIVED
    assert observation.target_hp.source is DataSource.DERIVED
    assert observation.target_level.source is DataSource.DERIVED
    assert observation.distance.source is DataSource.DERIVED
    assert observation.in_combat.source is DataSource.DERIVED
    assert observation.hp.value == 7305
    assert observation.mp.value == 1420
    assert observation.target_hp.value == 64
    assert observation.target_level.value == 18
    assert observation.distance.value == 12.5
    assert observation.in_combat.value is True
    assert observation.target_is_enemy.value is True
    assert observation.target_is_dead.value is False
    assert observation.published_value_errors() == []
    assert unknown_published_value_errors(observation.to_wire()) == []


def test_valid_packet_accepts_flat_bytes():
    packet = _valid_packet(hp=256, mp=1, target_hp=0, target_level=1, distance=0.0, in_combat=False)
    flat = bytes(channel for block in packet for channel in block)
    observation = decode_pixel_bridge(flat)
    assert observation.hp.source is DataSource.DERIVED
    assert observation.hp.value == 256
    assert observation.mp.value == 1
    assert observation.target_hp.value == 0
    assert observation.distance.value == 0.0
    assert observation.in_combat.value is False


def test_truncated_packet_is_all_unknown():
    packet = _valid_packet()
    truncated = packet[:6]
    observation = decode_pixel_bridge(truncated)
    assert observation.hp.source is DataSource.UNKNOWN
    assert observation.mp.source is DataSource.UNKNOWN
    assert observation.target_hp.source is DataSource.UNKNOWN
    assert observation.distance.source is DataSource.UNKNOWN
    assert observation.in_combat.source is DataSource.UNKNOWN
    assert observation.hp.value is None
    assert observation.mp.value is None
    assert observation.target_hp.value is None
    assert observation.distance.value is None
    assert observation.in_combat.value is None
    assert observation.hp.failure_reason == "truncated_packet"
    assert observation.published_value_errors() == []


def test_truncated_bytes_and_short_flat_sequence_are_unknown():
    short_bytes = decode_pixel_bridge(b"\xff\x00\xff\x01")
    assert short_bytes.hp.failure_reason == "truncated_packet"
    assert short_bytes.hp.value is None

    short_flat = decode_pixel_bridge([255, 0, 255, 1, 0, 0])
    assert short_flat.mp.source is DataSource.UNKNOWN
    assert short_flat.mp.value is None


def test_missing_block_is_all_unknown():
    packet = list(_valid_packet())
    packet[3] = None
    observation = decode_pixel_bridge(packet)
    assert observation.hp.source is DataSource.UNKNOWN
    assert observation.mp.value is None
    assert observation.hp.failure_reason == "missing_block"
    assert list(iter_packet_failure_reasons(packet)) == ["missing_block"]


def test_none_packet_is_unknown():
    observation = decode_pixel_bridge(None)
    assert observation.hp.source is DataSource.UNKNOWN
    assert observation.hp.failure_reason == "missing_block"
    assert observation.hp.value is None


def test_color_out_of_scale_is_all_unknown():
    packet = list(_valid_packet())
    packet[1] = (256, 0, 0)
    observation = decode_pixel_bridge(packet)
    assert observation.hp.source is DataSource.UNKNOWN
    assert observation.hp.value is None
    assert observation.hp.failure_reason == "color_out_of_scale"
    assert observation.mp.value is None

    negative = list(_valid_packet())
    negative[2] = (-1, 0, 0)
    assert decode_pixel_bridge(negative).mp.failure_reason == "color_out_of_scale"


def test_invalid_anchor_is_all_unknown():
    packet = list(_valid_packet())
    packet[0] = (0, 0, 0)
    observation = decode_pixel_bridge(packet)
    assert observation.hp.source is DataSource.UNKNOWN
    assert observation.target_level.value is None
    assert observation.hp.failure_reason == "invalid_anchor"
    assert PIXEL_BRIDGE_ANCHOR_RGB == (255, 0, 255)
    assert PIXEL_BRIDGE_BLOCK_COUNT == 10


def test_implausible_target_hp_is_unknown_other_fields_stay_derived():
    packet = list(_valid_packet())
    packet[5] = (150, 0, 0)
    observation = decode_pixel_bridge(packet)
    assert observation.target_hp.source is DataSource.UNKNOWN
    assert observation.target_hp.value is None
    assert observation.target_hp.failure_reason == "implausible_target_hp"
    assert observation.hp.source is DataSource.DERIVED
    assert observation.hp.value == 7305
    assert observation.mp.source is DataSource.DERIVED
    assert observation.distance.source is DataSource.DERIVED
    assert observation.published_value_errors() == []


def test_skull_target_level_is_unknown_not_255():
    observation = decode_pixel_bridge(_valid_packet(target_level=255))
    assert observation.target_level.source is DataSource.UNKNOWN
    assert observation.target_level.value is None
    assert observation.target_level.failure_reason == "unknown_target_level"
    assert observation.hp.source is DataSource.DERIVED
    assert observation.target_hp.source is DataSource.DERIVED


def test_zero_vitals_are_derived_observations_not_unknown():
    observation = decode_pixel_bridge(
        _valid_packet(
            hp=0,
            mp=0,
            target_hp=0,
            target_level=0,
            distance=0.0,
            in_combat=False,
            target_is_enemy=False,
            target_is_dead=False,
        )
    )
    assert observation.hp.source is DataSource.DERIVED
    assert observation.hp.value == 0
    assert observation.mp.value == 0
    assert observation.target_hp.value == 0
    assert observation.distance.value == 0.0
    assert observation.in_combat.value is False


def test_combat_flag_uses_channel_presence_not_invented_state():
    combat = decode_pixel_bridge(_valid_packet(in_combat=True, target_is_dead=True))
    assert combat.in_combat.value is True
    assert combat.target_is_dead.value is True
    idle = decode_pixel_bridge(_valid_packet(in_combat=False, target_is_enemy=False))
    assert idle.in_combat.value is False
    assert idle.target_is_enemy.value is False


def test_sixteen_bit_split_round_trip():
    observation = decode_pixel_bridge(
        _valid_packet(hp=65535, mp=256, distance=100.0)
    )
    assert observation.hp.value == 65535
    assert observation.mp.value == 256
    assert observation.distance.value == 100.0
