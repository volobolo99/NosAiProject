from nosai.guard.runtime import TrustTier
from nosai.telemetry.advanced import AdvancedTelemetryCollector, TelemetryMetrics


def test_mastery_formula_is_bounded():
    assert AdvancedTelemetryCollector.calculate_mastery(100, 100, 100, 100, 100) == 100
    assert AdvancedTelemetryCollector.calculate_mastery(-10, 0, 0, 0, 0) == 0


def test_collector_average_and_snapshot():
    collector = AdvancedTelemetryCollector()
    collector.record_tick_metrics(TelemetryMetrics(1, 0, 90, 90, 100, 80, 100, 92, TrustTier.TIER_4, 0, 0))
    collector.record_tick_metrics(TelemetryMetrics(2, 1, 80, 90, 90, 80, 90, 86, TrustTier.TIER_3, 1, 1))
    assert collector.calculate_average_mastery_score() == 89
    assert len(collector.snapshot()) == 2
