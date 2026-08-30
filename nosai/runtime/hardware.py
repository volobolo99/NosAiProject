"""Hardware capability discovery contracts and deterministic runtime profiles."""
from __future__ import annotations

import os
from dataclasses import dataclass

from nosai.core.data_classification import ClassifiedValue


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
class RuntimeProfile:
    name: str
    max_parallel_agents: int
    context_tokens: int
    local_preferred: bool

class HardwareProfiler:
    def profile(self, h: HardwareSnapshot) -> RuntimeProfile:
        if h.vram_mb >= 24000 and h.ram_mb >= 32000:
            return RuntimeProfile("high", 4, 32768, True)
        if h.vram_mb >= 8000 and h.ram_mb >= 16000:
            return RuntimeProfile("balanced", 2, 16384, True)
        return RuntimeProfile("constrained", 1, 8192, True)


def classified_local_pc() -> dict[str, object]:
    """Gate 1 PC baseline: live values where the OS provides them, otherwise UNKNOWN."""
    threads = os.cpu_count()
    return {
        "cpu_threads": ClassifiedValue.live(threads).to_wire() if threads else ClassifiedValue.unknown("cpu_count_unavailable").to_wire(),
        "ram_mb": ClassifiedValue.unknown("system_ram_probe_not_available").to_wire(),
        "gpu_name": ClassifiedValue.unknown("gpu_probe_not_available").to_wire(),
        "vram_mb": ClassifiedValue.unknown("vram_probe_not_available").to_wire(),
        "temperature_c": ClassifiedValue.unknown("temperature_probe_not_available").to_wire(),
    }
