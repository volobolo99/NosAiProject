"""Hardware capability discovery and resource-aware runtime policy.

The profiler is deterministic: measured capabilities become bounded execution
budgets. Missing measurements remain UNKNOWN and are never fabricated.

``classified_local_pc()`` performs the actual Gate 1 hardware probing. RAM,
GPU name/VRAM and CPU/GPU temperature all come from optional third-party
packages (``psutil``, ``pynvml``) that may not be installed on every machine
this runtime runs on:

* ``psutil`` supplies system RAM and CPU temperature sensors.
* ``pynvml`` supplies NVIDIA GPU name, VRAM and GPU temperature via NVML.
  It is NVIDIA-only: there is no AMD/Intel equivalent wired up here, so a
  non-NVIDIA GPU (or an NVIDIA GPU without a loaded driver) reports UNKNOWN,
  never a guessed value.
* Free/total storage on the current volume uses ``shutil.disk_usage`` from
  the standard library, so it needs no optional dependency.

Every probe below follows the same shape already established by
``PsutilNvmlProbe`` in ``hardware_watchdog.py``: the import is lazy and
wrapped in a narrow ``try/except`` that degrades to UNKNOWN (never raises)
when the package is missing, the sensor is unsupported on this platform, or
the underlying call fails. A reading that comes back structurally impossible
(non-positive total memory, negative available/used memory) is also treated
as UNKNOWN rather than clamped to an invented number.
"""
from __future__ import annotations

import os
import shutil
from dataclasses import dataclass

from nosai.core.data_classification import ClassifiedValue
from nosai.runtime.hardware_watchdog import PsutilNvmlProbe


@dataclass(frozen=True)
class HardwareSnapshot:
    cpu_threads: int = 0
    ram_mb: int = 0
    gpu_name: str = "unknown"
    vram_mb: int = 0
    temperature_c: float = 0.0
    gpu_utilization: float = 0.0
    power_w: float = 0.0


@dataclass(frozen=True)
class InferenceBudget:
    """Bounded per-job resources for the autonomous runtime."""

    max_cpu_ms: int
    max_gpu_ms: int
    max_vram_mb: int
    max_ram_mb: int
    max_latency_ms: int
    max_concurrency: int


@dataclass(frozen=True)
class RuntimeProfile:
    name: str
    max_parallel_agents: int
    context_tokens: int
    local_preferred: bool
    inference_budget: InferenceBudget = InferenceBudget(4, 8, 512, 1024, 25, 1)


class HardwareProfiler:
    """Map hardware capabilities to conservative deterministic budgets."""

    def profile(self, h: HardwareSnapshot) -> RuntimeProfile:
        vram = max(0, h.vram_mb)
        ram = max(0, h.ram_mb)

        if vram >= 24000 and ram >= 32000:
            return RuntimeProfile(
                "high", 4, 32768, True,
                InferenceBudget(12, 25, 4096, 8192, 40, 2),
            )
        if vram >= 8000 and ram >= 16000:
            return RuntimeProfile(
                "balanced", 2, 16384, True,
                InferenceBudget(6, 14, 2048, 3072, 30, 1),
            )
        return RuntimeProfile(
            "constrained", 1, 8192, True,
            InferenceBudget(3, 6, 768, 1536, 25, 1),
        )


def recommended_profile_for_nitro_v16(h: HardwareSnapshot) -> RuntimeProfile:
    """Return the bounded policy for the target laptop without hardcoding SKU details."""
    return HardwareProfiler().profile(h)


def _unknown_pair(reason: str) -> tuple[ClassifiedValue, ClassifiedValue]:
    return ClassifiedValue.unknown(reason), ClassifiedValue.unknown(reason)


def _unknown_triple(reason: str) -> tuple[ClassifiedValue, ClassifiedValue, ClassifiedValue]:
    return ClassifiedValue.unknown(reason), ClassifiedValue.unknown(reason), ClassifiedValue.unknown(reason)


def _probe_system_ram() -> tuple[ClassifiedValue, ClassifiedValue]:
    """Total/available system RAM in MB, via ``psutil.virtual_memory()``.

    Same exception handling as ``PsutilNvmlProbe``: ``psutil`` is optional,
    imported lazily, and any failure degrades to UNKNOWN instead of raising.
    """
    try:
        import psutil

        vm = psutil.virtual_memory()
        total_mb = int(vm.total // (1024 * 1024))
        available_mb = int(vm.available // (1024 * 1024))
    except (ImportError, AttributeError, OSError):
        return _unknown_pair("system_ram_probe_not_available")

    if total_mb <= 0:
        # A non-positive total is not a real reading; never clamp it to a
        # made-up positive number.
        return _unknown_pair("system_ram_probe_returned_non_positive_value")

    total = ClassifiedValue.live(total_mb)
    if available_mb < 0:
        available = ClassifiedValue.unknown("system_ram_available_probe_returned_negative_value")
    else:
        available = ClassifiedValue.live(available_mb)
    return total, available


def _probe_nvidia_gpu() -> tuple[ClassifiedValue, ClassifiedValue, ClassifiedValue]:
    """Primary NVIDIA GPU name, total VRAM and used VRAM in MB, via ``pynvml``.

    NVIDIA-only: NVML has no AMD/Intel equivalent in this codebase, so a
    non-NVIDIA GPU is reported UNKNOWN (reason ``no_nvidia_gpu_detected``)
    rather than guessed. ``pynvml`` itself is optional and imported lazily.
    """
    try:
        import pynvml
    except ImportError:
        return _unknown_triple("pynvml_not_installed")

    # pynvml is bound from here on, so its own error hierarchy
    # (``pynvml.NVMLError`` and subclasses, e.g. "driver not loaded" or
    # "library not found") can be referenced safely. It is not a subclass of
    # OSError/RuntimeError, so it must be listed explicitly: without this an
    # installed-but-driverless pynvml (no NVIDIA GPU present, or the driver
    # not loaded) raises unhandled out of this probe.
    nvml_exceptions = (AttributeError, OSError, RuntimeError, pynvml.NVMLError)

    try:
        pynvml.nvmlInit()
    except nvml_exceptions:
        return _unknown_triple("nvidia_driver_not_available")

    name = None
    total_mb = None
    used_mb = None
    try:
        count = pynvml.nvmlDeviceGetCount()
        if count > 0:
            handle = pynvml.nvmlDeviceGetHandleByIndex(0)
            raw_name = pynvml.nvmlDeviceGetName(handle)
            name = raw_name.decode("utf-8", "replace") if isinstance(raw_name, bytes) else str(raw_name)
            mem = pynvml.nvmlDeviceGetMemoryInfo(handle)
            total_mb = int(mem.total // (1024 * 1024))
            used_mb = int(mem.used // (1024 * 1024))
    except nvml_exceptions:
        name = total_mb = used_mb = None
    finally:
        try:
            pynvml.nvmlShutdown()
        except nvml_exceptions:
            pass

    if name is None or total_mb is None:
        return _unknown_triple("no_nvidia_gpu_detected")
    if not name or total_mb <= 0:
        # A blank name or non-positive VRAM total is not a real reading.
        return _unknown_triple("gpu_probe_returned_invalid_value")

    used = (
        ClassifiedValue.live(used_mb)
        if used_mb is not None and used_mb >= 0
        else ClassifiedValue.unknown("vram_used_probe_returned_negative_value")
    )
    return ClassifiedValue.live(name), ClassifiedValue.live(total_mb), used


def _probe_temperatures() -> tuple[ClassifiedValue, ClassifiedValue]:
    """CPU/GPU temperature in Celsius.

    Reuses ``PsutilNvmlProbe`` (the same optional psutil/pynvml probe that
    backs ``NOSAIHardwareWatchdog``) instead of duplicating its detection
    logic.
    """
    telemetry = PsutilNvmlProbe().read()
    cpu = (
        ClassifiedValue.live(telemetry.cpu_temperature_c)
        if telemetry.cpu_temperature_c is not None
        else ClassifiedValue.unknown("cpu_temperature_probe_not_available")
    )
    gpu = (
        ClassifiedValue.live(telemetry.gpu_temperature_c)
        if telemetry.gpu_temperature_c is not None
        else ClassifiedValue.unknown("gpu_temperature_probe_not_available")
    )
    return cpu, gpu


def _probe_storage() -> tuple[ClassifiedValue, ClassifiedValue]:
    """Free/total space in MB on the volume backing the current working
    directory, via ``shutil.disk_usage`` (standard library, no optional
    dependency)."""
    try:
        usage = shutil.disk_usage(os.getcwd())
        free_mb = int(usage.free // (1024 * 1024))
        total_mb = int(usage.total // (1024 * 1024))
    except OSError:
        return _unknown_pair("storage_probe_not_available")

    if total_mb <= 0 or free_mb < 0:
        return _unknown_pair("storage_probe_returned_invalid_value")

    return ClassifiedValue.live(free_mb), ClassifiedValue.live(total_mb)


def classified_local_pc() -> dict[str, object]:
    """Gate 1 PC baseline: live values where a probe can read them, UNKNOWN
    (with an explicit structured reason) everywhere else.

    ``cpu_threads`` uses ``os.cpu_count()`` (standard library, always
    attempted). RAM and CPU temperature use ``psutil`` when installed. GPU
    name, VRAM and GPU temperature use ``pynvml`` when installed and an
    NVIDIA GPU with a loaded driver is present. Storage free/total space
    uses ``shutil.disk_usage``. None of these ever raise out of this
    function; an unavailable or nonsensical reading becomes UNKNOWN.
    """
    threads = os.cpu_count()
    ram_total, ram_available = _probe_system_ram()
    gpu_name, vram_total, vram_used = _probe_nvidia_gpu()
    cpu_temp, gpu_temp = _probe_temperatures()
    storage_free, storage_total = _probe_storage()

    return {
        "cpu_threads": (
            ClassifiedValue.live(threads).to_wire()
            if threads
            else ClassifiedValue.unknown("cpu_count_unavailable").to_wire()
        ),
        "ram_mb": ram_total.to_wire(),
        "ram_available_mb": ram_available.to_wire(),
        "gpu_name": gpu_name.to_wire(),
        "vram_mb": vram_total.to_wire(),
        "vram_used_mb": vram_used.to_wire(),
        "temperature_c": cpu_temp.to_wire(),
        "gpu_temperature_c": gpu_temp.to_wire(),
        "storage_free_mb": storage_free.to_wire(),
        "storage_total_mb": storage_total.to_wire(),
    }
