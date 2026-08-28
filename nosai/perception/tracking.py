"""Temporal entity tracking primitives for perception."""
from __future__ import annotations
from dataclasses import dataclass
from math import hypot
from typing import Sequence
from .contracts import BoundingBox, TrackedEntity

@dataclass
class Track:
    label: str
    x: float
    y: float
    vx: float = 0.0
    vy: float = 0.0
    confidence: float = 0.0

class CentroidTracker:
    """Deterministic nearest-neighbour tracker; replaceable by a Kalman backend."""
    def __init__(self, max_distance: float = 100.0) -> None:
        self.max_distance = max_distance
        self.tracks: list[Track] = []
        self._timestamp: float | None = None

    def update(self, detections: Sequence[BoundingBox], timestamp: float) -> Sequence[TrackedEntity]:
        dt = max(1e-6, timestamp - self._timestamp) if self._timestamp is not None else 1.0
        updated: list[Track] = []
        unmatched = list(self.tracks)
        for d in detections:
            cx, cy = d.x + d.width / 2, d.y + d.height / 2
            best = min(unmatched, key=lambda t: hypot(t.x - cx, t.y - cy), default=None)
            if best is not None and hypot(best.x - cx, best.y - cy) <= self.max_distance:
                unmatched.remove(best)
                best.vx, best.vy = (cx - best.x) / dt, (cy - best.y) / dt
                best.x, best.y, best.confidence = cx, cy, d.confidence
                updated.append(best)
            else:
                updated.append(Track(d.label, cx, cy, confidence=d.confidence))
        self.tracks = updated
        self._timestamp = timestamp
        return tuple(TrackedEntity(t.label, t.x, t.y, t.vx, t.vy, t.confidence) for t in self.tracks)
