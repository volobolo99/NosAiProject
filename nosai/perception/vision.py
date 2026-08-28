"""Lightweight ROI/HSV vision primitives.

These primitives are client-agnostic and operate on RGB/BGR byte frames supplied
by an adapter. They intentionally do not capture the desktop themselves.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import Sequence
from .contracts import BoundingBox, Frame

@dataclass(frozen=True)
class ROI:
    x: int
    y: int
    width: int
    height: int

@dataclass(frozen=True)
class HSVRange:
    h_min: int
    h_max: int
    s_min: int = 0
    s_max: int = 255
    v_min: int = 0
    v_max: int = 255

class ROIVision:
    """Defines stable regions of interest for downstream detectors."""
    def __init__(self, regions: Sequence[ROI] = ()) -> None:
        self.regions = tuple(regions)

    def crop_boxes(self, frame: Frame) -> tuple[ROI, ...]:
        return tuple(
            ROI(max(0, r.x), max(0, r.y), min(r.width, frame.width - max(0, r.x)),
                min(r.height, frame.height - max(0, r.y)))
            for r in self.regions if r.x < frame.width and r.y < frame.height
        )

    @staticmethod
    def box_from_roi(roi: ROI, label: str = "roi", confidence: float = 1.0) -> BoundingBox:
        return BoundingBox(roi.x, roi.y, roi.width, roi.height, confidence, label)
