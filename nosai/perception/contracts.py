"""Transport-neutral perception contracts."""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Optional

@dataclass(frozen=True)
class Frame:
    width: int
    height: int
    timestamp: float
    pixels: bytes

@dataclass(frozen=True)
class BoundingBox:
    x: float
    y: float
    width: float
    height: float
    confidence: float
    label: str

@dataclass(frozen=True)
class OCRResult:
    text: str
    confidence: float
    source: str
    roi: Optional[tuple[int,int,int,int]] = None

@dataclass(frozen=True)
class TrackedEntity:
    label: str
    x: float
    y: float
    vx: float
    vy: float
    confidence: float

@dataclass(frozen=True)
class PerceptionSnapshot:
    timestamp: float
    player_hp_pct: Optional[float] = None
    player_mp_pct: Optional[float] = None
    target_hp_pct: Optional[float] = None
    entities: tuple[TrackedEntity, ...] = field(default_factory=tuple)
    ocr: tuple[OCRResult, ...] = field(default_factory=tuple)
