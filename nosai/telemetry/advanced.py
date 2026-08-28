"""Thread-safe advanced telemetry and mastery metrics."""
from __future__ import annotations
from dataclasses import dataclass
import json
from pathlib import Path
from threading import Lock
from typing import Iterable
from nosai.guard.runtime import TrustTier

@dataclass(frozen=True)
class TelemetryMetrics:
    timestamp_ticks: int
    cycle_index: int
    xp_yield_rate: float
    time_efficiency_ratio: float
    safety_factor: float
    resource_conservation_index: float
    objective_completion_metric: float
    global_mastery_score: float
    active_trust_tier: TrustTier
    veto_count: int
    recovery_count: int

class AdvancedTelemetryCollector:
    def __init__(self) -> None:
        self._history: list[TelemetryMetrics] = []
        self._lock = Lock()

    def record_tick_metrics(self, metrics: TelemetryMetrics) -> None:
        with self._lock:
            self._history.append(metrics)

    def snapshot(self) -> tuple[TelemetryMetrics, ...]:
        with self._lock:
            return tuple(self._history)

    def export_jsonl(self, file_path: str | Path) -> None:
        path = Path(file_path)
        with self._lock:
            rows = list(self._history)
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("w", encoding="utf-8") as writer:
            for metric in rows:
                row = {
                    "timestamp_ticks": metric.timestamp_ticks,
                    "cycle_index": metric.cycle_index,
                    "xp_yield_rate": metric.xp_yield_rate,
                    "time_efficiency_ratio": metric.time_efficiency_ratio,
                    "safety_factor": metric.safety_factor,
                    "resource_conservation_index": metric.resource_conservation_index,
                    "objective_completion_metric": metric.objective_completion_metric,
                    "global_mastery_score": metric.global_mastery_score,
                    "active_trust_tier": metric.active_trust_tier.value,
                    "veto_count": metric.veto_count,
                    "recovery_count": metric.recovery_count,
                }
                writer.write(json.dumps(row, separators=(",", ":")) + "\n")

    def calculate_average_mastery_score(self) -> float:
        with self._lock:
            if not self._history:
                return 0.0
            return sum(m.global_mastery_score for m in self._history) / len(self._history)

    @staticmethod
    def calculate_mastery(xp: float, efficiency: float, safety: float,
                          resources: float, objective: float) -> float:
        """Default 5-dimension weighted mastery score, bounded to 0..100."""
        score = 0.30 * xp + 0.20 * efficiency + 0.20 * safety + 0.15 * resources + 0.15 * objective
        return max(0.0, min(100.0, score))
