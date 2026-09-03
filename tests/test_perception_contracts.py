from nosai.core.data_classification import ClassifiedValue, DataSource, unknown_published_value_errors
from nosai.perception.contracts import (
    FrameObservation,
    OcrObservation,
    PixelBridgeObservation,
    TemplateObservation,
    YoloDetection,
    YoloObservation,
    classify_frame,
    classify_ocr,
    classify_template,
    classify_yolo,
    derived_value,
    unknown_value,
)
from nosai.perception.vision import (
    UnavailableOcrSource,
    UnavailableScreenshotSource,
    UnavailableTemplateSource,
    UnavailableYoloSource,
)


def test_frame_observation_is_derived_when_complete():
    frame = classify_frame(width=800, height=600, pixel_format="rgb8", frame_id=3)
    assert frame.is_complete()
    assert frame.width.source is DataSource.DERIVED
    assert frame.height.source is DataSource.DERIVED
    assert frame.pixel_format.source is DataSource.DERIVED
    assert frame.width.value == 800
    assert unknown_published_value_errors(frame.to_wire()) == []


def test_incomplete_frame_is_unknown_not_zero():
    frame = classify_frame(width=None, height=0, pixel_format=None, frame_id=None)
    assert not frame.is_complete()
    assert frame.width.source is DataSource.UNKNOWN
    assert frame.height.source is DataSource.UNKNOWN
    assert frame.width.value is None
    assert frame.height.value is None
    assert frame.width.failure_reason == "incomplete_frame"
    assert unknown_published_value_errors(frame.to_wire()) == []


def test_ocr_observation_is_derived_when_text_is_read():
    observation = classify_ocr("7305/7305", confidence=0.91, region=(10, 20, 80, 12))
    assert observation.text.source is DataSource.DERIVED
    assert observation.confidence.source is DataSource.DERIVED
    assert observation.text.value == "7305/7305"
    assert observation.confidence.value == 0.91
    assert unknown_published_value_errors(observation.to_wire()) == []


def test_ocr_missing_or_invalid_is_unknown():
    missing = classify_ocr(None, confidence=0.5)
    assert missing.text.source is DataSource.UNKNOWN
    assert missing.text.value is None
    assert missing.text.failure_reason == "ocr_unavailable"

    invalid = classify_ocr("x", confidence=1.5)
    assert invalid.text.source is DataSource.UNKNOWN
    assert invalid.confidence.source is DataSource.UNKNOWN
    assert invalid.confidence.value is None
    assert unknown_published_value_errors(invalid.to_wire()) == []


def test_template_match_and_no_match_are_derived():
    hit = classify_template(
        matched=True, score=0.96, location=(4, 8, 16, 16), template_id="hp-icon"
    )
    assert hit.matched.source is DataSource.DERIVED
    assert hit.matched.value is True
    assert hit.location.value == (4, 8, 16, 16)

    miss = classify_template(
        matched=False, score=0.11, location=None, template_id="hp-icon"
    )
    assert miss.matched.source is DataSource.DERIVED
    assert miss.matched.value is False
    assert miss.location.source is DataSource.UNKNOWN
    assert miss.location.value is None


def test_template_invalid_score_or_missing_backend_is_unknown():
    invalid = classify_template(
        matched=True, score=2.0, location=(0, 0, 1, 1), template_id="hp-icon"
    )
    assert invalid.matched.source is DataSource.UNKNOWN
    assert invalid.score.value is None

    missing = classify_template(
        matched=None, score=None, location=None, template_id=None
    )
    assert missing.template_id.source is DataSource.UNKNOWN
    assert unknown_published_value_errors(missing.to_wire()) == []


def test_yolo_empty_batch_is_derived_unavailable_is_unknown():
    empty = classify_yolo(())
    assert empty.detections.source is DataSource.DERIVED
    assert empty.detections.value == ()

    unavailable = classify_yolo(None)
    assert unavailable.detections.source is DataSource.UNKNOWN
    assert unavailable.detections.value is None
    assert unavailable.detections.failure_reason == "yolo_unavailable"

    box = YoloDetection(
        label=derived_value("creature"),
        confidence=derived_value(0.7),
        box=derived_value((0.1, 0.2, 0.3, 0.4)),
    )
    found = classify_yolo((box,))
    assert found.detections.source is DataSource.DERIVED
    assert found.detections.value[0].label.value == "creature"
    assert unknown_published_value_errors(found.to_wire()) == []


def test_perception_contracts_reject_live_provenance():
    live = ClassifiedValue.live(10)
    try:
        FrameObservation(
            width=live,
            height=derived_value(1),
            pixel_format=derived_value("rgb8"),
            frame_id=derived_value(0),
        )
        raised = False
    except ValueError as error:
        raised = True
        assert "DERIVED or UNKNOWN" in str(error)
    assert raised


def test_unknown_factory_does_not_publish_values():
    for observation in (
        FrameObservation.unknown("incomplete_frame"),
        PixelBridgeObservation.unknown("truncated_packet"),
        OcrObservation.unknown("ocr_unavailable"),
        TemplateObservation.unknown("template_unavailable"),
        YoloObservation.unknown("yolo_unavailable"),
    ):
        assert unknown_published_value_errors(observation.to_wire()) == []
        if isinstance(observation, YoloObservation):
            assert observation.detections.value is None
        else:
            first = next(iter(observation.to_wire().values()))
            assert first["source"] == DataSource.UNKNOWN.value
            assert first["value"] is None


def test_unavailable_vision_sources_return_unknown_not_invented_readings():
    frame = UnavailableScreenshotSource().capture_frame()
    assert frame.width.source is DataSource.UNKNOWN
    assert frame.width.value is None

    complete = classify_frame(width=320, height=240, pixel_format="rgb8", frame_id=1)
    ocr = UnavailableOcrSource().read_text(complete)
    template = UnavailableTemplateSource().match(complete, "hp-icon")
    yolo = UnavailableYoloSource().detect(complete)
    assert ocr.text.source is DataSource.UNKNOWN
    assert template.matched.source is DataSource.UNKNOWN
    assert yolo.detections.source is DataSource.UNKNOWN
    assert ocr.text.value is None
    assert template.matched.value is None
    assert yolo.detections.value is None

    incomplete = classify_frame(width=None, height=None)
    assert UnavailableOcrSource().read_text(incomplete).text.failure_reason == "incomplete_frame"
    assert UnavailableTemplateSource().match(incomplete, "hp-icon").matched.failure_reason == (
        "incomplete_frame"
    )
    assert UnavailableYoloSource().detect(incomplete).detections.failure_reason == (
        "incomplete_frame"
    )


def test_unknown_value_helper_matches_core_classification():
    classified = unknown_value("missing_block")
    assert classified.source is DataSource.UNKNOWN
    assert classified.has_value is False
    assert classified.has_observed_value is False
    assert classified.value is None
    assert classified.failure_reason == "missing_block"
