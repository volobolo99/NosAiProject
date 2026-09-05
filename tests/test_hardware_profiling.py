"""Real coverage for the hardened Gate 1 hardware probes in
``nosai.runtime.hardware``.

``psutil``/``pynvml`` stay optional in production code (lazy import inside a
narrow try/except, exactly as ``PsutilNvmlProbe`` already does in
``hardware_watchdog.py``). These tests simulate their absence/presence only
at the test boundary via ``sys.modules`` — production code never mocks
anything itself.
"""
from __future__ import annotations

import sys
from types import SimpleNamespace

import pytest

from nosai.core.data_classification import DataSource
from nosai.runtime.hardware import classified_local_pc
from nosai.runtime.hardware_watchdog import PsutilNvmlProbe


class _FakeNVMLError(Exception):
    """Stand-in for ``pynvml.NVMLError``: NOT a RuntimeError/OSError subclass,
    matching the real library (this is exactly what the production code must
    catch explicitly to avoid an unhandled exception)."""


def _fake_psutil(total_bytes: int, available_bytes: int, cpu_temp_c: float | None = 55.0):
    temps = {"coretemp": [SimpleNamespace(current=cpu_temp_c)]} if cpu_temp_c is not None else {}
    return SimpleNamespace(
        virtual_memory=lambda: SimpleNamespace(total=total_bytes, available=available_bytes),
        sensors_temperatures=lambda: temps,
    )


def _fake_pynvml(
    *,
    name: object = "NVIDIA GeForce RTX 5060 Laptop GPU",
    total_bytes: int = 8192 * 1024 * 1024,
    used_bytes: int = 1024 * 1024 * 1024,
    count: int = 1,
    gpu_temp_c: float = 61.0,
    init_raises: Exception | None = None,
    count_raises: Exception | None = None,
):
    def nvml_init():
        if init_raises is not None:
            raise init_raises

    def device_count():
        if count_raises is not None:
            raise count_raises
        return count

    return SimpleNamespace(
        NVMLError=_FakeNVMLError,
        NVML_TEMPERATURE_GPU=0,
        nvmlInit=nvml_init,
        nvmlDeviceGetCount=device_count,
        nvmlDeviceGetHandleByIndex=lambda i: i,
        nvmlDeviceGetName=lambda h: name,
        nvmlDeviceGetMemoryInfo=lambda h: SimpleNamespace(total=total_bytes, used=used_bytes),
        nvmlDeviceGetTemperature=lambda h, kind: gpu_temp_c,
        nvmlShutdown=lambda: None,
    )


def _no_optional_deps(monkeypatch: pytest.MonkeyPatch) -> None:
    # sys.modules[name] = None forces `import name` to raise ImportError,
    # regardless of whether the package happens to be installed in the
    # environment actually running this test suite.
    monkeypatch.setitem(sys.modules, "psutil", None)
    monkeypatch.setitem(sys.modules, "pynvml", None)


def _with_deps(monkeypatch: pytest.MonkeyPatch, psutil_mod=None, pynvml_mod=None) -> None:
    monkeypatch.setitem(sys.modules, "psutil", psutil_mod if psutil_mod is not None else _fake_psutil(0, 0))
    monkeypatch.setitem(sys.modules, "pynvml", pynvml_mod if pynvml_mod is not None else _fake_pynvml())


# --- absent psutil/pynvml: everything degrades to UNKNOWN, never raises ----


def test_all_optional_fields_unknown_without_psutil_or_pynvml(monkeypatch):
    _no_optional_deps(monkeypatch)

    snapshot = classified_local_pc()  # must not raise

    for key in (
        "ram_mb", "ram_available_mb", "gpu_name", "vram_mb", "vram_used_mb",
        "temperature_c", "gpu_temperature_c",
    ):
        assert snapshot[key]["source"] == DataSource.UNKNOWN.value, key
        assert snapshot[key]["value"] is None, key
        assert snapshot[key]["failureReason"]
    # cpu_threads (os.cpu_count) and storage_*_mb (shutil.disk_usage) rely
    # only on the standard library, so they are unaffected by psutil/pynvml
    # being absent.
    assert snapshot["cpu_threads"]["source"] in {DataSource.LIVE.value, DataSource.UNKNOWN.value}
    assert snapshot["storage_free_mb"]["source"] in {DataSource.LIVE.value, DataSource.UNKNOWN.value}


def test_ram_unknown_reason_is_explicit_when_psutil_missing(monkeypatch):
    _no_optional_deps(monkeypatch)
    snapshot = classified_local_pc()
    assert snapshot["ram_mb"]["failureReason"] == "system_ram_probe_not_available"


def test_gpu_unknown_reason_is_explicit_when_pynvml_missing(monkeypatch):
    _no_optional_deps(monkeypatch)
    snapshot = classified_local_pc()
    assert snapshot["gpu_name"]["failureReason"] == "pynvml_not_installed"
    assert snapshot["vram_mb"]["failureReason"] == "pynvml_not_installed"


# --- present psutil/pynvml: live values populated correctly ---------------


def test_ram_is_live_and_converted_to_mb_when_psutil_available(monkeypatch):
    one_gb = 1024 * 1024 * 1024
    _with_deps(monkeypatch, psutil_mod=_fake_psutil(total_bytes=16 * one_gb, available_bytes=10 * one_gb))
    monkeypatch.setitem(sys.modules, "pynvml", None)

    snapshot = classified_local_pc()

    assert snapshot["ram_mb"]["source"] == DataSource.LIVE.value
    assert snapshot["ram_mb"]["value"] == 16384
    assert snapshot["ram_available_mb"]["source"] == DataSource.LIVE.value
    assert snapshot["ram_available_mb"]["value"] == 10240


def test_cpu_temperature_is_live_when_psutil_reports_a_sensor(monkeypatch):
    _with_deps(monkeypatch, psutil_mod=_fake_psutil(0, 0, cpu_temp_c=57.5))
    monkeypatch.setitem(sys.modules, "pynvml", None)

    snapshot = classified_local_pc()

    assert snapshot["temperature_c"]["source"] == DataSource.LIVE.value
    assert snapshot["temperature_c"]["value"] == 57.5


def test_gpu_name_and_vram_are_live_when_pynvml_reports_a_device(monkeypatch):
    one_mb = 1024 * 1024
    monkeypatch.setitem(sys.modules, "psutil", None)
    _with_deps(
        monkeypatch,
        pynvml_mod=_fake_pynvml(
            name="NVIDIA GeForce RTX 5060 Laptop GPU",
            total_bytes=8192 * one_mb,
            used_bytes=1500 * one_mb,
            gpu_temp_c=63.0,
        ),
    )

    snapshot = classified_local_pc()

    assert snapshot["gpu_name"]["source"] == DataSource.LIVE.value
    assert snapshot["gpu_name"]["value"] == "NVIDIA GeForce RTX 5060 Laptop GPU"
    assert snapshot["vram_mb"]["source"] == DataSource.LIVE.value
    assert snapshot["vram_mb"]["value"] == 8192
    assert snapshot["vram_used_mb"]["source"] == DataSource.LIVE.value
    assert snapshot["vram_used_mb"]["value"] == 1500
    assert snapshot["gpu_temperature_c"]["source"] == DataSource.LIVE.value
    assert snapshot["gpu_temperature_c"]["value"] == 63.0


def test_gpu_name_decodes_bytes_returned_by_older_pynvml_bindings(monkeypatch):
    monkeypatch.setitem(sys.modules, "psutil", None)
    _with_deps(monkeypatch, pynvml_mod=_fake_pynvml(name=b"NVIDIA GeForce RTX 5060 Laptop GPU"))

    snapshot = classified_local_pc()

    assert snapshot["gpu_name"]["source"] == DataSource.LIVE.value
    assert snapshot["gpu_name"]["value"] == "NVIDIA GeForce RTX 5060 Laptop GPU"


# --- absurd/negative readings degrade to UNKNOWN, never clamp -------------


def test_non_positive_total_ram_is_unknown_not_clamped(monkeypatch):
    _with_deps(monkeypatch, psutil_mod=_fake_psutil(total_bytes=0, available_bytes=0))
    monkeypatch.setitem(sys.modules, "pynvml", None)

    snapshot = classified_local_pc()

    assert snapshot["ram_mb"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["ram_mb"]["value"] is None
    assert snapshot["ram_mb"]["failureReason"] == "system_ram_probe_returned_non_positive_value"


def test_negative_available_ram_is_unknown_while_total_stays_live(monkeypatch):
    one_gb = 1024 * 1024 * 1024
    _with_deps(monkeypatch, psutil_mod=_fake_psutil(total_bytes=16 * one_gb, available_bytes=-1))
    monkeypatch.setitem(sys.modules, "pynvml", None)

    snapshot = classified_local_pc()

    assert snapshot["ram_mb"]["source"] == DataSource.LIVE.value
    assert snapshot["ram_available_mb"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["ram_available_mb"]["value"] is None


def test_non_positive_vram_total_is_unknown_not_clamped(monkeypatch):
    monkeypatch.setitem(sys.modules, "psutil", None)
    _with_deps(monkeypatch, pynvml_mod=_fake_pynvml(total_bytes=0))

    snapshot = classified_local_pc()

    assert snapshot["vram_mb"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["vram_mb"]["value"] is None
    assert snapshot["gpu_name"]["source"] == DataSource.UNKNOWN.value


def test_negative_used_vram_is_unknown_while_total_stays_live(monkeypatch):
    one_mb = 1024 * 1024
    monkeypatch.setitem(sys.modules, "psutil", None)
    _with_deps(monkeypatch, pynvml_mod=_fake_pynvml(total_bytes=8192 * one_mb, used_bytes=-one_mb))

    snapshot = classified_local_pc()

    assert snapshot["vram_mb"]["source"] == DataSource.LIVE.value
    assert snapshot["vram_used_mb"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["vram_used_mb"]["value"] is None


# --- pynvml installed but no usable NVIDIA device: no unhandled exception -


def test_gpu_unknown_when_no_nvidia_device_is_present(monkeypatch):
    monkeypatch.setitem(sys.modules, "psutil", None)
    _with_deps(monkeypatch, pynvml_mod=_fake_pynvml(count=0))

    snapshot = classified_local_pc()

    assert snapshot["gpu_name"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["gpu_name"]["failureReason"] == "no_nvidia_gpu_detected"


def test_gpu_unknown_when_pynvml_installed_but_driver_unavailable(monkeypatch):
    # Regression test: pynvml.NVMLError is not a RuntimeError/OSError
    # subclass. Before the fix this raised unhandled out of
    # classified_local_pc() whenever pynvml was importable but no NVIDIA
    # driver/library was actually present (a real, reproducible case).
    monkeypatch.setitem(sys.modules, "psutil", None)
    _with_deps(monkeypatch, pynvml_mod=_fake_pynvml(init_raises=_FakeNVMLError("NVML Shared Library Not Found")))

    snapshot = classified_local_pc()  # must not raise

    assert snapshot["gpu_name"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["gpu_name"]["failureReason"] == "nvidia_driver_not_available"
    assert snapshot["gpu_temperature_c"]["source"] == DataSource.UNKNOWN.value


def test_psutil_nvml_probe_survives_nvml_error_on_init(monkeypatch):
    # Direct regression test against the shared probe consumed by
    # NOSAIHardwareWatchdog.
    monkeypatch.setitem(sys.modules, "psutil", None)
    monkeypatch.setitem(
        sys.modules,
        "pynvml",
        _fake_pynvml(init_raises=_FakeNVMLError("NVML Shared Library Not Found")),
    )

    telemetry = PsutilNvmlProbe().read()  # must not raise

    assert telemetry.gpu_temperature_c is None


def test_gpu_unknown_when_device_count_lookup_fails(monkeypatch):
    monkeypatch.setitem(sys.modules, "psutil", None)
    _with_deps(monkeypatch, pynvml_mod=_fake_pynvml(count_raises=_FakeNVMLError("device query failed")))

    snapshot = classified_local_pc()  # must not raise

    assert snapshot["gpu_name"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["gpu_name"]["failureReason"] == "no_nvidia_gpu_detected"


# --- storage probe (stdlib-only) -------------------------------------------


def test_storage_is_live_from_shutil_disk_usage(monkeypatch):
    import nosai.runtime.hardware as hardware_module

    monkeypatch.setattr(
        hardware_module.shutil,
        "disk_usage",
        lambda path: SimpleNamespace(total=2_000_000_000_000, used=1_000_000_000_000, free=1_000_000_000_000),
    )

    snapshot = classified_local_pc()

    assert snapshot["storage_free_mb"]["source"] == DataSource.LIVE.value
    assert snapshot["storage_free_mb"]["value"] > 0
    assert snapshot["storage_total_mb"]["source"] == DataSource.LIVE.value


def test_storage_unknown_when_disk_usage_raises(monkeypatch):
    import nosai.runtime.hardware as hardware_module

    def _raise(_path):
        raise OSError("volume not available")

    monkeypatch.setattr(hardware_module.shutil, "disk_usage", _raise)

    snapshot = classified_local_pc()  # must not raise

    assert snapshot["storage_free_mb"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["storage_free_mb"]["failureReason"] == "storage_probe_not_available"


def test_storage_unknown_when_disk_usage_reports_non_positive_total(monkeypatch):
    import nosai.runtime.hardware as hardware_module

    monkeypatch.setattr(
        hardware_module.shutil,
        "disk_usage",
        lambda path: SimpleNamespace(total=0, used=0, free=0),
    )

    snapshot = classified_local_pc()

    assert snapshot["storage_total_mb"]["source"] == DataSource.UNKNOWN.value
    assert snapshot["storage_total_mb"]["failureReason"] == "storage_probe_returned_invalid_value"


# --- deterministic wire shape, always ---------------------------------------


def test_snapshot_is_serializable_wire_shaped_dict_regardless_of_deps(monkeypatch):
    _no_optional_deps(monkeypatch)
    snapshot = classified_local_pc()
    for key, field in snapshot.items():
        assert set(field.keys()) == {
            "value", "source", "observedAtUtc", "hasObservedValue", "warning", "failureReason",
        }, key
