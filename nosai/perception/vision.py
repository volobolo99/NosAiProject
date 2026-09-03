"""Lightweight source interfaces for future screenshot, OCR, template and YOLO backends.

No capture, OCR, OpenCV or YOLO library is imported here. A backend that is
not configured returns UNKNOWN. Sources observe; they do not send keyboard,
mouse or client input.
"""
from __future__ import annotations

from typing import Protocol

from nosai.perception.contracts import (
    FrameObservation,
    OcrObservation,
    TemplateObservation,
    YoloObservation,
    classify_frame,
    classify_ocr,
    classify_template,
    classify_yolo,
)


class ScreenshotSource(Protocol):
    """Produces classified frame metadata. Must not drive input."""

    def capture_frame(self) -> FrameObservation: ...


class OcrSource(Protocol):
    """Reads text from a previously captured frame."""

    def read_text(self, frame: FrameObservation) -> OcrObservation: ...


class TemplateSource(Protocol):
    """Matches a named template against a previously captured frame."""

    def match(self, frame: FrameObservation, template_id: str) -> TemplateObservation: ...


class YoloSource(Protocol):
    """Runs a detector on a previously captured frame."""

    def detect(self, frame: FrameObservation) -> YoloObservation: ...


class UnavailableScreenshotSource:
    """Default screenshot source until a backend is wired."""

    def capture_frame(self) -> FrameObservation:
        return FrameObservation.unknown("screenshot_backend_not_configured")


class UnavailableOcrSource:
    """Default OCR source. Refuses to invent text from an incomplete frame."""

    def read_text(self, frame: FrameObservation) -> OcrObservation:
        if not frame.is_complete():
            return OcrObservation.unknown("incomplete_frame")
        return OcrObservation.unknown("ocr_backend_not_configured")


class UnavailableTemplateSource:
    """Default template source. A missing backend is UNKNOWN, not a no-match."""

    def match(self, frame: FrameObservation, template_id: str) -> TemplateObservation:
        if not frame.is_complete():
            return TemplateObservation.unknown("incomplete_frame")
        if not template_id:
            return TemplateObservation.unknown("template_id_missing")
        return TemplateObservation.unknown("template_backend_not_configured")


class UnavailableYoloSource:
    """Default detector source. Unavailable is UNKNOWN, not an empty detection list."""

    def detect(self, frame: FrameObservation) -> YoloObservation:
        if not frame.is_complete():
            return YoloObservation.unknown("incomplete_frame")
        return YoloObservation.unknown("yolo_backend_not_configured")


def observe_unavailable_stack() -> tuple[
    UnavailableScreenshotSource,
    UnavailableOcrSource,
    UnavailableTemplateSource,
    UnavailableYoloSource,
]:
    """Return the four no-op sources used until real backends are attached."""
    return (
        UnavailableScreenshotSource(),
        UnavailableOcrSource(),
        UnavailableTemplateSource(),
        UnavailableYoloSource(),
    )


__all__ = [
    "OcrSource",
    "ScreenshotSource",
    "TemplateSource",
    "UnavailableOcrSource",
    "UnavailableScreenshotSource",
    "UnavailableTemplateSource",
    "UnavailableYoloSource",
    "YoloSource",
    "classify_frame",
    "classify_ocr",
    "classify_template",
    "classify_yolo",
    "observe_unavailable_stack",
]
