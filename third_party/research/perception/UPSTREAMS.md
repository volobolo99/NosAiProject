# Perception / Vision / OCR / Tracking — Upstream Research

## PaddlePaddle/PaddleOCR
- License: Apache-2.0 (verified)
- Role: OCR engine and document/text recognition research.
- NosAi use: quest/dialog/inventory UI OCR; prefer package/tool use over vendoring.
- Target: Perception OCR adapter, ROI-only invocation, confidence/provenance retained.
- Priority: VERY HIGH.

## open-mmlab/mmdetection
- License: Apache-2.0 (verified)
- Role: object detection toolbox and model zoo.
- NosAi use: detector training/evaluation reference; export selected models to ONNX for runtime.
- Target: offline training pipeline + Perception detector.
- Priority: HIGH.

## FoundationVision/ByteTrack
- License: MIT (verified)
- Role: multi-object tracking.
- NosAi use: temporal stabilization of mobs/NPCs/drops/interactables across frames.
- Target: Perception Tracking stage.
- Priority: VERY HIGH.

## facebookresearch/sam2
- License: Apache-2.0 (verified)
- Role: segmentation and video object tracking.
- NosAi use: primarily offline annotation/label propagation; optional selective runtime research.
- Target: dataset tooling, not mandatory live runtime.
- Priority: MEDIUM-HIGH.

## robmikh/Win32CaptureSample
- License: MIT (verified)
- Upstream commit: 49fefe79fd9b11025f0b5eb91783a98888516070
- Local reference: third_party/sources/robmikh/Win32CaptureSample/reference/
- Role: Windows.Graphics.Capture / D3D11 frame pool examples.
- NosAi use: low-latency window capture, CreateFreeThreaded frame pools, resize handling, snapshot capture.
- Target: src/NosAi.Runtime/Perception/Capture/
- Priority: VERY HIGH.
