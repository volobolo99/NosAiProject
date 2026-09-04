"""Hardware capability discovery and resource-aware runtime policy.

The profiler is deterministic: measured capabilities become bounded execution
budgets. Missing measurements remain UNKNOWN and are never fabricated.
"""
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
