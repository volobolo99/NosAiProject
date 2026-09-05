# NosAiProject — Autonomous Player Architecture Baseline

**Version:** 2.1
**Date:** 2026-09-05
**Status:** CANONICAL

## 1. Purpose

This document defines the architecture for NosAiProject as an autonomous player running on a Windows PC in a private educational/test environment.

The target is operational autonomy: perception, world modelling, mapping, navigation, combat, quest reasoning, character progression, memory, learning, recovery and verification.

The project is PC/Windows-first. Smartphone/mobile components are not part of the product architecture.

## 2. Canonical pipeline

```text
Client / PC Environment
        │
        ▼
Observation Sources
(Network | Memory | Screen | Local | Hardware | Software)
        │
        ▼
Sensor Fusion + Provenance
        │
        ▼
Unified World Model
        │
        ├────────► Spatial/Map Model
        ├────────► Combat Model
        ├────────► Quest Model
        └────────► Character/Inventory Model
        │
        ▼
Prediction / Local Simulation
        │
        ▼
Utility / Risk Ranking
        │
        ▼
Strategic Orchestrator
        │
        ▼
HTN / Deterministic GOAP / Reactive Rules / AI Reasoning
        │
        ▼
Guard → Trust/Authorization → Safety Gate
        │
        ▼
Execution Adapter
        │
        ▼
Verification
        │
        └──────────────► Re-observation / Learning
```

## 3. Layers

### Observation
NosAi può sfruttare le tecnologie disponibili nel proprio ambiente di esecuzione: rete, memoria del client e del sistema locale, screen capture, pixel, OCR/CV, filesystem, processi, finestre, API Windows, hardware, periferiche, software, librerie, telemetry e altri strumenti tecnicamente disponibili.

Sono esclusi esclusivamente gli accessi privilegiati al server definiti nella sezione 10.

### Perception
Transforms raw observations into typed observations with provenance, timestamp, confidence and freshness. Sensor disagreement reduces confidence rather than silently selecting convenient data.

### World Model
Single semantic state for planning. Important facts distinguish `LIVE`, `DERIVED`, `CACHED`, `SIMULATED` and `UNKNOWN`.

### Spatial Model
Maintains map identity, coordinate system, observed bounds, walkability, landmarks, portals, obstacles, map transitions and persistent versions. Supports coarse/global routing plus local movement planning.

### Domain Models
Combat, quest, character, inventory, equipment, resources, NPCs, mobs, drops and interactables are first-class models linked to WorldState.

### Planning
Strategic selection chooses goals; HTN decomposes complex objectives; deterministic cost-aware GOAP selects executable action sequences; reactive rules handle immediate interruptions and recovery. AI/LLM reasoning may assist planning but does not bypass Guard, Trust or Safety.

### Simulation
Short-horizon deterministic simulation estimates consequences before consequential actions. Simulation is advisory and never authoritative gameplay truth.

### Learning and Memory
Persistent memory records observations, decisions, outcomes, failures and learned effectiveness. Retrieval is provenance/freshness aware. ML/RL/world-model experiments are sandboxed/offline until validated.

### Safety
Safety is the final execution authority. No LLM, ML model, planner or UI component can bypass it.

### Execution
Execution adapters are replaceable. NosAi may use any technically appropriate local/system technology available to the runtime, subject to the absolute server-access prohibitions in section 10.

### Verification
Every consequential action must produce an observable result or explicit failure/unknown outcome and feed the next decision cycle.

## 4. PC hardware target

Baseline target:

- ASUS Nitro V16 laptop;
- AMD Ryzen processor;
- NVIDIA GeForce RTX 5060 Laptop GPU, 8 GB GDDR7 class;
- 16 GB DDR5 system RAM;
- external 2 TB SSD dedicated to NosAiProject and its runtime data.

The exact ASUS SKU, CPU model, RAM topology, GPU power envelope, driver version and external SSD USB mode must be detected at runtime rather than hardcoded.

NVIDIA lists the RTX 5060 Laptop GPU with 8 GB GDDR7, 3328 CUDA cores and a 45–100 W GPU subsystem range depending on laptop implementation. The architecture therefore treats VRAM and power/thermal headroom as dynamic capabilities, not fixed guarantees.

## 5. Resource strategy

### CPU
Prioritize WorldState, planning, navigation queries, orchestration, persistence and lightweight preprocessing.

### GPU
Prioritize vision inference, object detection, OCR where GPU acceleration is available, embeddings and other batchable inference. Model selection must respect the actual available VRAM.

### RAM
16 GB is the primary system-memory constraint. Avoid loading several large models simultaneously. Use bounded queues, streaming, memory-mapped datasets where useful, explicit caches and aggressive disposal of large frame buffers.

### External SSD
The SSD is the canonical project/runtime storage target. Separate hot runtime data from cold research/archive data. SQLite WAL, replay, model caches and map datasets must use bounded retention and checkpoint policies. Performance tests must measure real USB connection mode and latency rather than assuming internal-NVMe characteristics.

### Thermal/power
The laptop is a constrained thermal system. Hardware profiling must expose CPU/GPU temperature, utilization, clocks, memory pressure and power state where safely available. In thermal degradation, inference frequency and non-critical background work must reduce before critical control is affected.

## 6. AI execution policy

Use adaptive inference tiers:

```text
Tier 0 — deterministic rules / geometry / cached models
Tier 1 — lightweight local ML inference
Tier 2 — GPU accelerated vision/embeddings
Tier 3 — expensive local reasoning/model inference
```

The runtime chooses the cheapest tier that satisfies the confidence requirement. Expensive inference must never block safety, recovery or time-critical control indefinitely.

## 7. Capture and perception policy

Windows Graphics Capture is a preferred screen/window capture abstraction when supported. Capture should use frame pools and bounded processing queues. ROI processing is preferred over full-frame inference whenever the target region is known.

OCR is treated as one sensor, not as ground truth. Text extracted from quest/dialog/inventory UI must be associated with source region, timestamp and confidence.

## 8. Navigation policy

The navigation architecture should evolve from the current prototype grid planner toward a hierarchical representation inspired by Recast/Detour/DotRecast capabilities:

```text
Map observation
 → geometry/walkability
 → tiled spatial representation
 → global graph/navmesh
 → local path corridor
 → obstacle avoidance
 → movement execution
 → verification
```

Persistent map versions must support partial discovery, invalidation and incremental refinement.

## 9. Performance invariants

- no unbounded queues on critical paths;
- bounded frame retention;
- no synchronous expensive inference in Safety/Guard;
- cancellation and timeout on all long-running inference/planning operations;
- deterministic planning for identical state/configuration;
- benchmark p50/p95/p99 latency and allocation rate;
- thermal degradation is observable and fail-safe;
- external SSD throughput/latency is measured on the real laptop.

## 10. Access boundary — absolute server prohibitions

NosAi may use the technology and computing capabilities available in its environment. There is no artificial architectural restriction against advanced local technology, reverse engineering, network analysis, memory inspection, debugging, profiling, computer vision, automation, GPU/NPU/CPU computation, Windows APIs, storage, peripherals or other technical methods.

The following are **absolutely prohibited** and must never become dependencies of NosAi:

1. administrator credentials or administrator access data for the game server;
2. GM, moderator, administrator or equivalent privileged server accounts;
3. privileged server login credentials or access tokens;
4. server database credentials;
5. direct access to the game server database;
6. private server credentials, keys or tokens whose purpose is to obtain one of the privileged accesses above.

The prohibition is specifically about privileged server administration and direct server-database access. Local technology remains available to the project.

## 11. Verification hierarchy

```text
Present → Integrated → Done → Verified
```

`Verified` requires evidence from the applicable real/test environment. Source presence alone is never verification.

## 12. Product boundary

NosAi is a Windows PC autonomous-player system. Smartphone, mobile app and PC↔smartphone communication are removed from the product scope and are not required by any runtime gate or operational flow.
