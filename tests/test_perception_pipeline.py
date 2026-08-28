from nosai.perception import Frame, BoundingBox, OCRResult, PerceptionPipeline

class Vision:
    def detect(self, frame):
        return [BoundingBox(10, 20, 30, 40, 0.9, "monster")]

class OCR:
    def read(self, frame):
        return [OCRResult("Raid", 0.95, "test")]

class Tracker:
    def update(self, detections, timestamp):
        return []

def test_perception_pipeline_fuses_vision_and_ocr():
    snapshot = PerceptionPipeline(Vision(), OCR(), Tracker()).process(Frame(100, 100, 1.0, b"frame"))
    assert snapshot.timestamp == 1.0
    assert snapshot.entities == ()
    assert snapshot.ocr[0].text == "Raid"
