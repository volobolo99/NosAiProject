import pytest

from nosai.runtime.adaptive_throttling import AdaptiveThrottler, ThrottleMode


def test_normal_plan():
    plan = AdaptiveThrottler().evaluate(gpu_temperature_c=60, ram_ratio=0.5)
    assert plan.mode is ThrottleMode.NORMAL
    assert plan.perception_scale == 1.0
    assert plan.allow_noncritical is True


def test_cooling_plan():
    plan = AdaptiveThrottler().evaluate(gpu_temperature_c=82, ram_ratio=0.5)
    assert plan.mode is ThrottleMode.COOLING
    assert plan.perception_scale < 1.0


def test_stopped_on_lan_disconnect():
    plan = AdaptiveThrottler().evaluate(lan_disconnected_ms=2001)
    assert plan.mode is ThrottleMode.STOPPED
    assert plan.allow_noncritical is False


def test_invalid_memory_ratio():
    with pytest.raises(ValueError):
        AdaptiveThrottler().evaluate(ram_ratio=1.1)
