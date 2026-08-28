"""Perception system public API."""
from .contracts import Frame, BoundingBox, OCRResult, TrackedEntity, PerceptionSnapshot
from .pipeline import PerceptionPipeline, VisionBackend, OCRBackend, TrackingBackend

__all__ = [
    "Frame", "BoundingBox", "OCRResult", "TrackedEntity", "PerceptionSnapshot",
    "PerceptionPipeline", "VisionBackend", "OCRBackend", "TrackingBackend",
]
