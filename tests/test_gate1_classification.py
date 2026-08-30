from datetime import datetime, timezone

from nosai.core.data_classification import ClassifiedValue, DataSource
from nosai.dashboard.server import GATE1_CONTRACT_VERSION, runtime_snapshot
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


def test_unsupported_contract_version_is_not_treated_as_live(monkeypatch):
    monkeypatch.setattr(
        "nosai.dashboard.server.fetch_gate1_snapshot",
        lambda: (None, "unsupported_contract_version:gate1.snapshot.v2"),
    )
    snapshot = runtime_snapshot()
    assert snapshot["connected"] is False
    assert snapshot["telemetry_source"] == "UNKNOWN"
    assert snapshot["gate1"] is None
    assert snapshot["gate1_failure"] == "unsupported_contract_version:gate1.snapshot.v2"


def test_matching_contract_version_is_accepted(monkeypatch):
    payload = {"contractVersion": GATE1_CONTRACT_VERSION, "runtimeStatus": "Degraded"}
    monkeypatch.setattr("nosai.dashboard.server.fetch_gate1_snapshot", lambda: (payload, None))
    snapshot = runtime_snapshot()
    assert snapshot["connected"] is True
    assert snapshot["telemetry_source"] == "LIVE"
    assert snapshot["gate1"] is payload
    assert snapshot["gate1_failure"] is None


def test_simulated_is_never_labelled_live():
    simulated = ClassifiedValue.simulated(68.5)
    assert simulated.source is DataSource.SIMULATED
    assert simulated.to_wire()["source"] == "SIMULATED"
    assert simulated.to_wire()["value"] == 68.5


def test_cached_keeps_its_original_observation_time():
    observed = datetime(2026, 8, 30, 12, 0, 0, tzinfo=timezone.utc)
    cached = ClassifiedValue.cached("x", observed)
    assert cached.source is DataSource.CACHED
    assert cached.to_wire()["observedAtUtc"] == "2026-08-30T12:00:00.0000000Z"


def test_observed_none_is_distinguishable_from_never_observed():
    # An observed value that happens to be None still counts as observed; only
    # has_observed_value separates it from a reading that never happened.
    observed_none = ClassifiedValue(None, DataSource.LIVE, datetime.now(timezone.utc))
    never = ClassifiedValue.unknown("not_read")
    assert observed_none.has_value is True
    assert observed_none.to_wire()["hasObservedValue"] is True
    assert never.has_value is False
    assert never.to_wire()["hasObservedValue"] is False


def test_wire_timestamp_matches_the_csharp_representation():
    # Seven fractional digits plus a literal Z, as emitted by the C# side.
    wire = ClassifiedValue.live(1).to_wire()["observedAtUtc"]
    assert wire.endswith("Z")
    assert len(wire.split(".")[1]) == 8
