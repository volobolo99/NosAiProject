from nosai.core.data_classification import ClassifiedValue, DataSource
from nosai.dashboard.server import runtime_snapshot
from nosai.runtime.hardware import classified_local_pc


def test_unknown_is_not_zero():
    unknown = ClassifiedValue.unknown("missing")
    assert unknown.value is None
    assert unknown.source is DataSource.UNKNOWN
    assert unknown.to_wire()["value"] is None
    assert unknown.to_wire()["source"] == "UNKNOWN"


def test_dashboard_does_not_invent_runtime_when_disconnected(monkeypatch):
    monkeypatch.setattr("nosai.dashboard.server.RUNTIME_URL", "")
    snapshot = runtime_snapshot()
    assert snapshot["connected"] is False
    assert snapshot["telemetry_source"] == "UNKNOWN"
    assert snapshot["provider"] == "not-connected"
    assert snapshot["gate1"] is None


def test_classified_local_pc_does_not_fake_ram_or_gpu():
    snapshot = classified_local_pc()
    assert snapshot["ram_mb"]["source"] == "UNKNOWN"
    assert snapshot["ram_mb"]["value"] is None
    assert snapshot["gpu_name"]["source"] == "UNKNOWN"
    assert snapshot["temperature_c"]["value"] is None
    assert snapshot["cpu_threads"]["source"] in {DataSource.LIVE.value, DataSource.UNKNOWN.value}
