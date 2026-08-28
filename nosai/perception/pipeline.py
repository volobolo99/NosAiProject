"""Deterministic perception pipeline skeleton.

The production DXGI/YOLO/OCR backends are intentionally injected behind these
contracts; this layer can run in tests and simulation without game I/O.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import Protocol, Sequence
from .contracts import Frame, BoundingBox, OCRResult, TrackedEntity, PerceptionSnapshot

class VisionBackend(Protocol):
    def detect(self, frame: Frame) -> Sequence[BoundingBox]: ...

class OCRBackend(Protocol):
    def read(self, frame: Frame) -> Sequence[OCRResult]: ...

class TrackingBackend(Protocol):
    def update(self, detections: Sequence[BoundingBox], timestamp: float) -> Sequence[TrackedEntity]: ...

@dataclass
class PerceptionPipeline:
    vision: VisionBackend
    ocr: OCRBackend
    tracker: TrackingBackend

    def process(self, frame: Frame) -> PerceptionSnapshot:
        detections = tuple(self.vision.detect(frame))
        ocr_results = tuple(self.ocr.read(frame))
        tracked = tuple(self.tracker.update(detections, frame.timestamp))
        return PerceptionSnapshot(
            timestamp=frame.timestamp,
            entities=tracked,
            ocr=ocr_results,
        )
