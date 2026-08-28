"""Perception system public API."""
from .contracts import Frame, BoundingBox, OCRResult, TrackedEntity, PerceptionSnapshot
from .pipeline import PerceptionPipeline, VisionBackend, OCRBackend, TrackingBackend
from .vision import ROI, HSVRange, ROIVision
from .tracking import Track, CentroidTracker
from .game_state import EvaluatedGameState, GameStateEvaluator

__all__ = [
    "Frame", "BoundingBox", "OCRResult", "TrackedEntity", "PerceptionSnapshot",
    "PerceptionPipeline", "VisionBackend", "OCRBackend", "TrackingBackend",
    "ROI", "HSVRange", "ROIVision", "Track", "CentroidTracker",
    "EvaluatedGameState", "GameStateEvaluator",
]
