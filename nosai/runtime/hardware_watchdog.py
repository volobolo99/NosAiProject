from __future__ import annotations
from dataclasses import dataclass
import time
from typing import Protocol

@dataclass(frozen=True)
class HardwareTelemetry:
    cpu_temperature_c: float | None = None
    gpu_temperature_c: float | None = None
    io_rate_mb_s: float | None = None

class HardwareProbe(Protocol):
    def read(self) -> HardwareTelemetry: ...

class NullHardwareProbe:
    def read(self) -> HardwareTelemetry:
        return HardwareTelemetry()

class PsutilNvmlProbe:
    def read(self) -> HardwareTelemetry:
        cpu = gpu = io_rate = None
        try:
            import psutil
            temps = psutil.sensors_temperatures()
            values = [x.current for group in temps.values() for x in group if x.current is not None]
            if values: cpu = max(values)
        except (ImportError, AttributeError, OSError): pass
        try:
            import pynvml
            pynvml.nvmlInit()
            values = [pynvml.nvmlDeviceGetTemperature(pynvml.nvmlDeviceGetHandleByIndex(i), pynvml.NVML_TEMPERATURE_GPU) for i in range(pynvml.nvmlDeviceGetCount())]
            if values: gpu = max(values)
        except (ImportError, AttributeError, OSError, RuntimeError): pass
        return HardwareTelemetry(cpu, gpu, io_rate)

@dataclass(frozen=True)
class WatchdogDecision:
    allowed: bool
    reason: str = "ok"
    cooling_seconds: float = 0.0

class NOSAIHardwareWatchdog:
    def __init__(self, max_temp: float = 80.0, max_io_rate_mb_s: float | None = None, cooling_seconds: float = 5.0, probe: HardwareProbe | None = None) -> None:
        self.max_temp = max_temp
        self.max_io_rate_mb_s = max_io_rate_mb_s
        self.cooling_seconds = max(0.0, cooling_seconds)
        self.probe = probe or NullHardwareProbe()
        self.cooling = False

    def check(self) -> WatchdogDecision:
        t = self.probe.read()
        temps = [x for x in (t.cpu_temperature_c, t.gpu_temperature_c) if x is not None]
        if any(x > self.max_temp for x in temps):
            self.cooling = True
            return WatchdogDecision(False, "thermal_limit", self.cooling_seconds)
        if self.max_io_rate_mb_s is not None and t.io_rate_mb_s is not None and t.io_rate_mb_s > self.max_io_rate_mb_s:
            self.cooling = True
            return WatchdogDecision(False, "io_rate_limit", self.cooling_seconds)
        self.cooling = False
        return WatchdogDecision(True)

    def verifica_integrita_hardware(self) -> bool:
        d = self.check()
        if not d.allowed and d.cooling_seconds:
            time.sleep(d.cooling_seconds)
        return d.allowed
