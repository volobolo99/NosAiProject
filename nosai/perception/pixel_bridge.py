"""Generic pixel-bridge decoder.

The packet is a sequence of 10 RGB triples. Encoding follows the split-byte
layout used by the WoW monitor addon (MyMonitor.lua): a magenta anchor, two
blocks per 16-bit integer (high then low on the red channel), a percent and
a level on the red channel, a three-channel flag block, and a 16-bit
centi-distance. This module only decodes and classifies; it does not capture
a screen or send input.

Channel values outside ``[0, 255]``, missing blocks, a truncated packet or a
non-magenta anchor make every field ``UNKNOWN``. A single implausible scalar
(target HP outside 0-100, skull level 255) makes that field ``UNKNOWN`` and
leaves independently valid neighbours ``DERIVED``.
"""
from __future__ import annotations

from typing import Iterable, Sequence

from nosai.perception.contracts import (
    PixelBridgeObservation,
    derived_value,
    unknown_value,
)

Rgb = tuple[int, int, int]
PacketLike = Sequence[Rgb] | Sequence[int] | Sequence[Sequence[int]] | bytes | bytearray

PIXEL_BRIDGE_BLOCK_COUNT = 10
PIXEL_BRIDGE_CHANNEL_MIN = 0
PIXEL_BRIDGE_CHANNEL_MAX = 255
PIXEL_BRIDGE_UINT16_MAX = 65535
PIXEL_BRIDGE_TARGET_HP_MAX = 100
PIXEL_BRIDGE_SKULL_LEVEL = 255
PIXEL_BRIDGE_DISTANCE_SCALE = 100.0
PIXEL_BRIDGE_ANCHOR_RGB: Rgb = (255, 0, 255)


class PixelBridgeBlock:
    """0-based indices of the generic 10-block packet."""

    ANCHOR = 0
    HP_HIGH = 1
    HP_LOW = 2
    MP_HIGH = 3
    MP_LOW = 4
    TARGET_HP = 5
    TARGET_LEVEL = 6
    FLAGS = 7
    DISTANCE_HIGH = 8
    DISTANCE_LOW = 9


def _is_channel(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and (
        PIXEL_BRIDGE_CHANNEL_MIN <= value <= PIXEL_BRIDGE_CHANNEL_MAX
    )


def _as_rgb(block: object) -> Rgb | None:
    if not isinstance(block, Sequence) or isinstance(block, (str, bytes, bytearray)):
        return None
    if len(block) != 3:
        return None
    red, green, blue = block[0], block[1], block[2]
    if not (_is_channel(red) and _is_channel(green) and _is_channel(blue)):
        return None
    return (int(red), int(green), int(blue))


def normalize_pixel_bridge_packet(packet: PacketLike | None) -> list[Rgb] | str:
    """Return 10 RGB triples or a failure reason.

    Variance: a packet shorter than 10 blocks, a block that is not a 3-tuple,
    or a channel outside ``[0, 255]`` cannot be decoded without inventing
    bytes, so the function returns a reason string instead of a partial list.
    """
    if packet is None:
        return "missing_block"
    if isinstance(packet, (bytes, bytearray)):
        if len(packet) < PIXEL_BRIDGE_BLOCK_COUNT * 3:
            return "truncated_packet"
        if len(packet) % 3 != 0:
            return "truncated_packet"
        values: Sequence[int] = list(packet)
        blocks: list[object] = [
            (values[index], values[index + 1], values[index + 2])
            for index in range(0, len(values), 3)
        ]
    elif len(packet) == 0:
        return "truncated_packet"
    elif _looks_flat(packet):
        values = packet  # type: ignore[assignment]
        if len(values) < PIXEL_BRIDGE_BLOCK_COUNT * 3:
            return "truncated_packet"
        if len(values) % 3 != 0:
            return "truncated_packet"
        blocks = [
            (values[index], values[index + 1], values[index + 2])
            for index in range(0, len(values), 3)
        ]
    else:
        blocks = list(packet)

    if len(blocks) < PIXEL_BRIDGE_BLOCK_COUNT:
        return "truncated_packet"

    rgb_blocks: list[Rgb] = []
    for block in blocks[:PIXEL_BRIDGE_BLOCK_COUNT]:
        if block is None:
            return "missing_block"
        rgb = _as_rgb(block)
        if rgb is None:
            if isinstance(block, Sequence) and not isinstance(block, (str, bytes, bytearray)):
                if len(block) != 3:
                    return "truncated_packet"
                if any(not _is_channel(channel) for channel in block):
                    return "color_out_of_scale"
            return "color_out_of_scale"
        rgb_blocks.append(rgb)
    return rgb_blocks


def _looks_flat(packet: Sequence[object]) -> bool:
    first = packet[0]
    return isinstance(first, int) and not isinstance(first, bool)


def encode_pixel_bridge_packet(
    *,
    hp: int,
    mp: int,
    target_hp: int,
    target_level: int,
    distance: float,
    in_combat: bool,
    target_is_enemy: bool = False,
    target_is_dead: bool = False,
) -> tuple[Rgb, ...]:
    """Encode known values into the 10-block packet. Does not invent fields."""
    if not 0 <= hp <= PIXEL_BRIDGE_UINT16_MAX:
        raise ValueError("hp does not fit the 16-bit pixel-bridge encoding")
    if not 0 <= mp <= PIXEL_BRIDGE_UINT16_MAX:
        raise ValueError("mp does not fit the 16-bit pixel-bridge encoding")
    if not 0 <= target_hp <= PIXEL_BRIDGE_TARGET_HP_MAX:
        raise ValueError("target_hp must be a 0-100 percent")
    if not 0 <= target_level <= PIXEL_BRIDGE_CHANNEL_MAX:
        raise ValueError("target_level must fit one channel")
    distance_centi = int(distance * PIXEL_BRIDGE_DISTANCE_SCALE)
    if not 0 <= distance_centi <= PIXEL_BRIDGE_UINT16_MAX:
        raise ValueError("distance does not fit the 16-bit centi-unit encoding")

    def split_uint16(value: int) -> tuple[Rgb, Rgb]:
        high, low = divmod(value, 256)
        return (high, 0, 0), (low, 0, 0)

    hp_high, hp_low = split_uint16(hp)
    mp_high, mp_low = split_uint16(mp)
    dist_high, dist_low = split_uint16(distance_centi)
    return (
        PIXEL_BRIDGE_ANCHOR_RGB,
        hp_high,
        hp_low,
        mp_high,
        mp_low,
        (target_hp, 0, 0),
        (target_level, 0, 0),
        (1 if in_combat else 0, 1 if target_is_enemy else 0, 1 if target_is_dead else 0),
        dist_high,
        dist_low,
    )


def decode_pixel_bridge(packet: PacketLike | None) -> PixelBridgeObservation:
    """Decode a packet into classified vitals.

    A structural failure (truncated, missing, out-of-scale, bad anchor)
    yields an all-UNKNOWN observation. Per-field plausibility failures
    isolate UNKNOWN to that field so a bad target percent does not erase
    an independently valid player HP.
    """
    normalized = normalize_pixel_bridge_packet(packet)
    if isinstance(normalized, str):
        return PixelBridgeObservation.unknown(normalized)
    if normalized[PixelBridgeBlock.ANCHOR] != PIXEL_BRIDGE_ANCHOR_RGB:
        return PixelBridgeObservation.unknown("invalid_anchor")

    hp = _decode_uint16(
        normalized[PixelBridgeBlock.HP_HIGH],
        normalized[PixelBridgeBlock.HP_LOW],
        field="hp",
    )
    mp = _decode_uint16(
        normalized[PixelBridgeBlock.MP_HIGH],
        normalized[PixelBridgeBlock.MP_LOW],
        field="mp",
    )
    target_hp = _decode_percent(normalized[PixelBridgeBlock.TARGET_HP])
    target_level = _decode_level(normalized[PixelBridgeBlock.TARGET_LEVEL])
    in_combat, target_is_enemy, target_is_dead = _decode_flags(
        normalized[PixelBridgeBlock.FLAGS]
    )
    distance = _decode_distance(
        normalized[PixelBridgeBlock.DISTANCE_HIGH],
        normalized[PixelBridgeBlock.DISTANCE_LOW],
    )
    return PixelBridgeObservation(
        hp=hp,
        mp=mp,
        target_hp=target_hp,
        target_level=target_level,
        distance=distance,
        in_combat=in_combat,
        target_is_enemy=target_is_enemy,
        target_is_dead=target_is_dead,
    )


def _decode_uint16(high: Rgb, low: Rgb, *, field: str):
    value = high[0] * 256 + low[0]
    if not 0 <= value <= PIXEL_BRIDGE_UINT16_MAX:
        return unknown_value(f"implausible_{field}")
    return derived_value(value)


def _decode_percent(block: Rgb):
    value = block[0]
    if not 0 <= value <= PIXEL_BRIDGE_TARGET_HP_MAX:
        return unknown_value("implausible_target_hp")
    return derived_value(value)


def _decode_level(block: Rgb):
    value = block[0]
    if value == PIXEL_BRIDGE_SKULL_LEVEL:
        return unknown_value("unknown_target_level")
    return derived_value(value)


def _decode_flags(block: Rgb):
    return (
        derived_value(block[0] > 0),
        derived_value(block[1] > 0),
        derived_value(block[2] > 0),
    )


def _decode_distance(high: Rgb, low: Rgb):
    centi = high[0] * 256 + low[0]
    if not 0 <= centi <= PIXEL_BRIDGE_UINT16_MAX:
        return unknown_value("implausible_distance")
    return derived_value(centi / PIXEL_BRIDGE_DISTANCE_SCALE)


def iter_packet_failure_reasons(packet: PacketLike | None) -> Iterable[str]:
    """Yield the structural reason, if any, for a packet that cannot be read."""
    reason = normalize_pixel_bridge_packet(packet)
    if isinstance(reason, str):
        yield reason
        return
    if reason[PixelBridgeBlock.ANCHOR] != PIXEL_BRIDGE_ANCHOR_RGB:
        yield "invalid_anchor"
