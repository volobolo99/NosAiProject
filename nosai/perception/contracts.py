"""Observation contracts for screen-derived perception.

Screen, pixel-bridge, OCR, template and YOLO readings are classified
``DERIVED`` when a validity check passes. A missing or invalid reading is
``UNKNOWN`` and never a substituted number, empty string or empty list.
``UNKNOWN`` is not zero, false or empty (ADR-0012).
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Sequence

from nosai.core.data_classification import ClassifiedValue, DataSource, unknown_published_value_errors


PERCEPTION_SOURCES = frozenset({DataSource.DERIVED, DataSource.UNKNOWN})


def _require_perception_source(name: str, classified: ClassifiedValue[Any]) -> None:
    """Reject LIVE/CACHED/SIMULATED on a screen-derived field.

    The mathematical constraint is the source set, not the payload: a
    screenshot cannot acquire LIVE merely because the pixels look stable.
    """
    if classified.source not in PERCEPTION_SOURCES:
        raise ValueError(
            f"{name} must be DERIVED or UNKNOWN, got {classified.source.value}"
        )
    if classified.source is DataSource.UNKNOWN and classified.value is not None:
        raise ValueError(f"{name} is UNKNOWN but published value={classified.value!r}")


def derived_value(value: Any, warning: str | None = None) -> ClassifiedValue[Any]:
    """Mark an observed screen-derived value as DERIVED."""
    return ClassifiedValue.derived(value, warning)


def unknown_value(reason: str, warning: str | None = None) -> ClassifiedValue[Any]:
    """Mark a missing or invalid reading as UNKNOWN with no published value."""
    return ClassifiedValue.unknown(reason, warning)


@dataclass(frozen=True)
class FrameObservation:
    """Classified metadata of one captured frame. No input side-effects."""

    width: ClassifiedValue[int]
    height: ClassifiedValue[int]
    pixel_format: ClassifiedValue[str]
    frame_id: ClassifiedValue[int]

    def __post_init__(self) -> None:
        for name in ("width", "height", "pixel_format", "frame_id"):
            _require_perception_source(name, getattr(self, name))

    @classmethod
    def unknown(cls, reason: str) -> "FrameObservation":
        missing = unknown_value(reason)
        return cls(width=missing, height=missing, pixel_format=missing, frame_id=missing)

    def is_complete(self) -> bool:
        """True only when both spatial extents were observed and positive."""
        return (
            self.width.has_value
            and self.height.has_value
            and self.width.value is not None
            and self.height.value is not None
            and self.width.value > 0
            and self.height.value > 0
        )

    def to_wire(self) -> dict[str, Any]:
        return {
            "width": self.width.to_wire(),
            "height": self.height.to_wire(),
            "pixelFormat": self.pixel_format.to_wire(),
            "frameId": self.frame_id.to_wire(),
        }


@dataclass(frozen=True)
class PixelBridgeObservation:
    """Decoded generic pixel-bridge packet.

    Fields match the 10-block layout documented in
    ``nosai.perception.pixel_bridge``. Absolute HP/MP are 16-bit integers;
    target HP is the encoded 0-100 percent; distance is the encoded range
    after dividing the 16-bit centi-unit by 100. No field is invented when
    a block is missing or fails validation.
    """

    hp: ClassifiedValue[int]
    mp: ClassifiedValue[int]
    target_hp: ClassifiedValue[int]
    target_level: ClassifiedValue[int]
    distance: ClassifiedValue[float]
    in_combat: ClassifiedValue[bool]
    target_is_enemy: ClassifiedValue[bool]
    target_is_dead: ClassifiedValue[bool]

    def __post_init__(self) -> None:
        for name in (
            "hp",
            "mp",
            "target_hp",
            "target_level",
            "distance",
            "in_combat",
            "target_is_enemy",
            "target_is_dead",
        ):
            _require_perception_source(name, getattr(self, name))

    @classmethod
    def unknown(cls, reason: str) -> "PixelBridgeObservation":
        missing = unknown_value(reason)
        return cls(
            hp=missing,
            mp=missing,
            target_hp=missing,
            target_level=missing,
            distance=missing,
            in_combat=missing,
            target_is_enemy=missing,
            target_is_dead=missing,
        )

    def to_wire(self) -> dict[str, Any]:
        return {
            "hp": self.hp.to_wire(),
            "mp": self.mp.to_wire(),
            "targetHp": self.target_hp.to_wire(),
            "targetLevel": self.target_level.to_wire(),
            "distance": self.distance.to_wire(),
            "inCombat": self.in_combat.to_wire(),
            "targetIsEnemy": self.target_is_enemy.to_wire(),
            "targetIsDead": self.target_is_dead.to_wire(),
        }

    def published_value_errors(self) -> list[str]:
        """UNKNOWN must not publish a value on the wire."""
        return unknown_published_value_errors(self.to_wire())


@dataclass(frozen=True)
class OcrObservation:
    """Text read from a frame region. DERIVED when the reader produced a result."""

    text: ClassifiedValue[str]
    confidence: ClassifiedValue[float]
    region: ClassifiedValue[tuple[int, int, int, int]]

    def __post_init__(self) -> None:
        for name in ("text", "confidence", "region"):
            _require_perception_source(name, getattr(self, name))

    @classmethod
    def unknown(cls, reason: str) -> "OcrObservation":
        missing = unknown_value(reason)
        return cls(text=missing, confidence=missing, region=missing)

    def to_wire(self) -> dict[str, Any]:
        return {
            "text": self.text.to_wire(),
            "confidence": self.confidence.to_wire(),
            "region": self.region.to_wire(),
        }


@dataclass(frozen=True)
class TemplateObservation:
    """Template-match result. A no-match is DERIVED(False), not UNKNOWN."""

    matched: ClassifiedValue[bool]
    score: ClassifiedValue[float]
    location: ClassifiedValue[tuple[int, int, int, int]]
    template_id: ClassifiedValue[str]

    def __post_init__(self) -> None:
        for name in ("matched", "score", "location", "template_id"):
            _require_perception_source(name, getattr(self, name))

    @classmethod
    def unknown(cls, reason: str) -> "TemplateObservation":
        missing = unknown_value(reason)
        return cls(matched=missing, score=missing, location=missing, template_id=missing)

    def to_wire(self) -> dict[str, Any]:
        return {
            "matched": self.matched.to_wire(),
            "score": self.score.to_wire(),
            "location": self.location.to_wire(),
            "templateId": self.template_id.to_wire(),
        }


@dataclass(frozen=True)
class YoloDetection:
    """One detector box. Each field is independently DERIVED or UNKNOWN."""

    label: ClassifiedValue[str]
    confidence: ClassifiedValue[float]
    box: ClassifiedValue[tuple[float, float, float, float]]

    def __post_init__(self) -> None:
        for name in ("label", "confidence", "box"):
            _require_perception_source(name, getattr(self, name))

    def to_wire(self) -> dict[str, Any]:
        return {
            "label": self.label.to_wire(),
            "confidence": self.confidence.to_wire(),
            "box": self.box.to_wire(),
        }


@dataclass(frozen=True)
class YoloObservation:
    """Detector output. An empty tuple is DERIVED (ran, found nothing)."""

    detections: ClassifiedValue[tuple[YoloDetection, ...]]

    def __post_init__(self) -> None:
        _require_perception_source("detections", self.detections)
        if self.detections.has_value and self.detections.value is not None:
            for index, detection in enumerate(self.detections.value):
                if not isinstance(detection, YoloDetection):
                    raise TypeError(f"detections[{index}] must be YoloDetection")

    @classmethod
    def unknown(cls, reason: str) -> "YoloObservation":
        return cls(detections=unknown_value(reason))

    def to_wire(self) -> dict[str, Any]:
        if not self.detections.has_value or self.detections.value is None:
            return {"detections": self.detections.to_wire()}
        return {
            "detections": {
                **self.detections.to_wire(),
                "value": [item.to_wire() for item in self.detections.value],
            }
        }


def classify_frame(
    *,
    width: int | None,
    height: int | None,
    pixel_format: str | None = None,
    frame_id: int | None = None,
) -> FrameObservation:
    """Build a frame observation, marking incomplete extents as UNKNOWN."""
    width_value = (
        derived_value(width)
        if width is not None and width > 0
        else unknown_value("incomplete_frame")
    )
    height_value = (
        derived_value(height)
        if height is not None and height > 0
        else unknown_value("incomplete_frame")
    )
    format_value = (
        derived_value(pixel_format)
        if pixel_format
        else unknown_value("incomplete_frame")
    )
    frame_id_value = (
        derived_value(frame_id)
        if frame_id is not None
        else unknown_value("incomplete_frame")
    )
    return FrameObservation(
        width=width_value,
        height=height_value,
        pixel_format=format_value,
        frame_id=frame_id_value,
    )


def classify_ocr(
    text: str | None,
    *,
    confidence: float | None,
    region: tuple[int, int, int, int] | None = None,
) -> OcrObservation:
    """Classify an OCR backend result without inventing text or confidence."""
    if text is None:
        return OcrObservation.unknown("ocr_unavailable")
    if confidence is None or not 0.0 <= confidence <= 1.0:
        return OcrObservation(
            text=unknown_value("invalid_ocr_confidence"),
            confidence=unknown_value("invalid_ocr_confidence"),
            region=(
                derived_value(region)
                if region is not None
                else unknown_value("ocr_region_missing")
            ),
        )
    return OcrObservation(
        text=derived_value(text),
        confidence=derived_value(confidence),
        region=(
            derived_value(region)
            if region is not None
            else unknown_value("ocr_region_missing")
        ),
    )


def classify_template(
    *,
    matched: bool | None,
    score: float | None,
    location: tuple[int, int, int, int] | None,
    template_id: str | None,
) -> TemplateObservation:
    """Classify a template-match result. Score outside [0, 1] is UNKNOWN."""
    if matched is None or template_id is None:
        return TemplateObservation.unknown("template_unavailable")
    if score is None or not 0.0 <= score <= 1.0:
        return TemplateObservation.unknown("invalid_template_score")
    if matched and location is None:
        return TemplateObservation.unknown("template_location_missing")
    return TemplateObservation(
        matched=derived_value(matched),
        score=derived_value(score),
        location=(
            derived_value(location)
            if location is not None
            else unknown_value("template_no_match")
        ),
        template_id=derived_value(template_id),
    )


def classify_yolo(
    detections: Sequence[YoloDetection] | None,
) -> YoloObservation:
    """Classify a detector batch. ``None`` is UNKNOWN; ``()`` is DERIVED."""
    if detections is None:
        return YoloObservation.unknown("yolo_unavailable")
    return YoloObservation(detections=derived_value(tuple(detections)))
