"""Hardware capability discovery contracts and deterministic runtime profiles."""
from dataclasses import dataclass

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
