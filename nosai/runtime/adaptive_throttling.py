"""Controllo adattivo del carico runtime basato su temperatura e memoria.

Il componente produce un piano di risorse; non esegue direttamente azioni sul
sistema operativo. L'applicazione del piano resta responsabilità del runtime.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class ThrottleMode(str, Enum):
    NORMAL = "NORMAL"
    COOLING = "COOLING"
    DEGRADED = "DEGRADED"
    STOPPED = "STOPPED"


@dataclass(frozen=True)
class AdaptiveLimits:
    normal_gpu_c: float = 75.0
    cooling_gpu_c: float = 80.0
    critical_gpu_c: float = 90.0
    max_ram_ratio: float = 0.92
    critical_ram_ratio: float = 0.98
    lan_disconnect_ms: int = 2000


@dataclass(frozen=True)
class ResourcePlan:
    mode: ThrottleMode
    perception_scale: float
    allow_noncritical: bool
    reason: str


class AdaptiveThrottler:
    """Calcola il livello operativo senza modificare direttamente l'esecuzione."""

    def __init__(self, limits: AdaptiveLimits | None = None) -> None:
        self.limits = limits or AdaptiveLimits()

    def evaluate(
        self,
        *,
        gpu_temperature_c: float | None = None,
        ram_ratio: float | None = None,
        lan_disconnected_ms: int = 0,
        critical_fault: bool = False,
    ) -> ResourcePlan:
        if ram_ratio is not None and not 0.0 <= ram_ratio <= 1.0:
            raise ValueError("ram_ratio deve essere compreso tra 0 e 1")
        if lan_disconnected_ms < 0:
            raise ValueError("lan_disconnected_ms non può essere negativo")

        if critical_fault or lan_disconnected_ms > self.limits.lan_disconnect_ms:
            return ResourcePlan(ThrottleMode.STOPPED, 0.0, False, "anomalia critica o disconnessione LAN")
        if gpu_temperature_c is not None and gpu_temperature_c >= self.limits.critical_gpu_c:
            return ResourcePlan(ThrottleMode.STOPPED, 0.0, False, "temperatura GPU critica")
        if ram_ratio is not None and ram_ratio >= self.limits.critical_ram_ratio:
            return ResourcePlan(ThrottleMode.STOPPED, 0.0, False, "memoria RAM critica")

        thermal = gpu_temperature_c is not None and gpu_temperature_c >= self.limits.cooling_gpu_c
        memory = ram_ratio is not None and ram_ratio >= self.limits.max_ram_ratio
        if thermal or memory:
            return ResourcePlan(ThrottleMode.COOLING, 0.5, False, "riduzione del carico per temperatura o memoria")

        return ResourcePlan(ThrottleMode.NORMAL, 1.0, True, "carico entro i limiti")
