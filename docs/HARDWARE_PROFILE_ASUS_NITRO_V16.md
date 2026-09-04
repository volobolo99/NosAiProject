# NosAiProject — Hardware Profile: ASUS Nitro V16

**Version:** 1.0
**Date:** 2026-09-05
**Status:** CANONICAL HARDWARE TARGET

## 1. Target machine

NosAiProject must be optimized first for the developer's ASUS Nitro V16 laptop.

Known baseline:

| Resource | Target |
|---|---|
| System RAM | 16 GB DDR5 |
| GPU | NVIDIA GeForce RTX 5060 Laptop GPU, 8 GB class |
| CPU | AMD Ryzen, exact SKU detected at runtime |
| Project storage | Dedicated external SSD, 2 TB |
| OS | Windows desktop |
| External automation devices | None required; mouse and keyboard permitted but optional |

The exact laptop SKU and CPU must be discovered by the Hardware Profiler. Do not encode an assumed Ryzen model or GPU TGP in application logic.

## 2. Design consequences

The machine is capable of meaningful local AI acceleration, but 16 GB system RAM and 8 GB GPU memory require explicit resource governance.

The RTX 5060 Laptop GPU is a Blackwell GPU with 8 GB GDDR7 and 3328 CUDA cores; NVIDIA documents laptop implementations across a 45–100 W GPU subsystem range. Therefore NosAi must detect the actual device, driver and power/thermal state rather than assuming desktop-class performance.

## 3. Runtime resource tiers

### Real-time tier

Always resident:
- WorldState;
- Safety/Guard;
- recovery/watchdog;
- action verification;
- compact perception features;
- bounded telemetry.

### Interactive AI tier

GPU/CPU budget:
- object detection;
- tracking;
- OCR;
- map feature extraction;
- embeddings when required.

### Background tier

Run opportunistically:
- memory consolidation;
- map optimization;
- replay analysis;
- dataset generation;
- model evaluation;
- offline learning.

Background work must yield under CPU/GPU/RAM/thermal pressure.

## 4. VRAM policy

With an 8 GB GPU ceiling:

1. never assume enough VRAM for multiple large models;
2. prefer one resident primary vision model;
3. use quantized/small models when accuracy remains sufficient;
4. unload or serialize secondary models when inactive;
5. process ROIs instead of full frames when possible;
6. use CPU fallback for low-rate non-critical inference;
7. expose model memory usage in telemetry;
8. reject a model configuration that cannot fit the detected budget.

The application must select inference backends dynamically. Windows ML can expose CPU and DirectML providers and can dynamically obtain compatible execution providers; this supports capability-based rather than hard-coded backend selection.

## 5. System RAM policy

16 GB DDR5 is treated as a constrained baseline.

Reserve memory for:
- Windows and desktop;
- NosTale client;
- NosAi runtime;
- database/cache;
- inference buffers.

Use:
- bounded channels;
- pooled buffers;
- frame dropping under pressure;
- lazy loading;
- streaming datasets;
- compressed/paged long-term memory;
- explicit retention limits for replay.

Do not keep full-resolution frame histories in RAM.

## 6. SSD policy

The dedicated 2 TB external SSD is the canonical NosAiProject storage location.

Suggested layout:

```text
NOSAI-SSD\NosAi\
  app\
  models\
  maps\
  memory\
  sqlite\
  replay\
  evidence\
  logs\
  cache\
  datasets\
  third_party\
```

Hot files should be separated from archival data. SQLite critical persistence uses WAL and FULL synchronous durability as already specified by the project. Replay/dataset retention must be bounded so the SSD cannot silently fill.

The runtime must identify the volume by stable identity/label and verify filesystem, availability and free space. It must not depend on a fixed drive letter.

## 7. Screen capture strategy

Use Windows.Graphics.Capture as the preferred capture path when supported. Capture frames through bounded frame pools and process only the regions needed by the active perception task. The capture layer must detect resize/device-loss conditions and recover without leaking frame resources.

## 8. AI scheduling

The scheduler should expose a budget such as:

```text
CPU budget
GPU budget
VRAM budget
RAM budget
thermal budget
SSD I/O budget
latency budget
```

Each inference job declares:
- priority;
- deadline;
- estimated CPU/GPU/RAM/VRAM cost;
- minimum confidence required;
- fallback strategy.

The scheduler prefers the lowest-cost computation that meets the confidence/deadline requirement.

## 9. Thermal and power adaptation

The laptop's thermal state is part of World/Runtime health, not merely telemetry.

When temperature, throttling or memory pressure rises:

1. reduce background jobs;
2. reduce perception frequency for non-critical regions;
3. reduce replay retention;
4. switch expensive inference to lighter models/backends;
5. preserve Guard, Safety and recovery;
6. safe-stop only when control reliability can no longer be guaranteed.

## 10. External-device boundary

Mouse and keyboard are permitted input devices but are optional execution backends. NosAi must remain architecturally capable of using software/client-side execution mechanisms that satisfy the non-privileged boundary.

No hardware macro device, programmable HID, USB automation device or other external actuator is required or permitted for the certified autonomous-player scenario.

## 11. Acceptance benchmarks

The hardware profile is considered validated only after measurements on the actual laptop:

- cold-start memory footprint;
- idle/active CPU utilization;
- GPU utilization;
- VRAM peak;
- system RAM peak;
- capture FPS and frame latency;
- OCR latency;
- detection latency;
- planning p50/p95/p99;
- SQLite write latency;
- external SSD read/write throughput and latency;
- thermal behavior during sustained autonomous operation.

Benchmarks must be stored with machine metadata, driver versions and model versions.

## 12. Sources

- NVIDIA RTX 50-series laptop specifications: official NVIDIA documentation.
- Windows Graphics Capture: official Microsoft documentation.
- Windows ML execution-provider documentation: official Microsoft documentation.
- DotRecast: reference navigation architecture; project license and provenance are tracked under `third_party/`.

These external sources inform architecture but do not override NosAiProject ADRs or the non-privileged boundary.
