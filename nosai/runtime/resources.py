"""Deterministic runtime resource accounting and hardware capability discovery."""
from dataclasses import dataclass
from typing import Callable

from .contracts import ResourceSnapshot


@dataclass(frozen=True)
class HardwareCapabilities:
    cpu_threads: int
    ram_mb: int
    gpu_available: bool
    vram_mb: int


class ResourceManager:
    def __init__(self, snapshot_provider: Callable[[], ResourceSnapshot] | None = None) -> None:
        self._provider = snapshot_provider or (lambda: ResourceSnapshot())

    def snapshot(self) -> ResourceSnapshot:
        return self._provider()

    def can_run_local(self, min_vram_mb: int = 512, max_temperature_c: float = 85.0) -> bool:
        s = self.snapshot()
        return s.vram_available_mb >= min_vram_mb and s.temperature_c <= max_temperature_c

    def budget_ok(self, max_power_w: float) -> bool:
        return self.snapshot().power_w <= max_power_w
