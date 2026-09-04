# NosAiProject — Autonomous Player / Hardware AI Research

**Date:** 2026-09-05
**Status:** Development guidance
**Scope:** ASUS Nitro V16 + 16 GB DDR5 + RTX 5060 Laptop 8 GB class + dedicated external 2 TB SSD

## Executive conclusion

The target machine is sufficient for a serious local autonomous-player stack if computation is tiered and bounded. The architecture should not attempt to keep many large models resident. Deterministic systems must own the real-time path; GPU inference should be concentrated on perception and other batchable workloads; expensive reasoning and learning should run opportunistically or offline.

## 1. GPU strategy

NVIDIA's current RTX 5060 Laptop specification lists 3328 CUDA cores, 8 GB GDDR7 and a laptop GPU subsystem range of 45–100 W depending on implementation. This makes the exact laptop power/thermal profile an important runtime capability.

Implication: use capability detection and adaptive model selection. Do not assume desktop RTX 5060 performance or a fixed TGP.

Recommended workload placement:

- GPU: object detection, visual embeddings, selected OCR/vision models and batched inference;
- CPU: WorldState, fusion, planning, navigation queries, persistence, orchestration;
- background: memory consolidation, replay analysis, dataset generation and offline learning.

## 2. Windows capture

Windows.Graphics.Capture is the preferred supported abstraction for capturing an application window/display. Microsoft documents frame pools and capture sessions and explicitly supports desktop Windows. The capture subsystem should use bounded queues and handle resize/device-loss conditions.

Implication for NosAi: capture the client directly where possible, then process ROIs instead of continuously sending the entire frame through expensive models.

## 3. AI inference backend

Windows ML is the strategic Windows-side abstraction for ONNX execution-provider selection. Microsoft documents CPU and DirectML providers and dynamic provider acquisition for compatible hardware. DirectML remains supported but is in maintenance while new Windows ML development is emphasized.

Implication: implement an `IInferenceBackend`/capability layer instead of hard-coding CUDA/DirectML. At startup, benchmark or validate available providers and select a backend per model.

## 4. Perception architecture

Use a multi-sensor design rather than a single CV model:

```text
Network observation ─┐
Client memory ────────┼→ Timestamped observations → Fusion → WorldState
Screen capture ──────┤
OCR / detector ───────┤
Local telemetry ──────┘
```

Each observation has provenance, timestamp, confidence and freshness. Sensor disagreement produces uncertainty rather than arbitrary truth selection.

Vision should use a cascade:

1. cheap deterministic ROI/change detection;
2. lightweight detector/tracker;
3. OCR only when text regions are relevant;
4. expensive semantic model only when ambiguity remains.

This is important for 16 GB RAM / 8 GB VRAM.

## 5. Mapping and navigation

DotRecast is the strongest current local reference for the C# architecture. It provides Recast navmesh generation, Detour runtime queries, tiled navmesh/streaming, crowd handling and dynamic navigation components. Its ZLib license is compatible with deliberate integration subject to provenance tracking.

Implication: replace the current simple grid planner over time with a project-owned navigation abstraction supporting:

- tiled spatial representation;
- walkability reconstruction;
- global graph/navmesh;
- local path corridor;
- dynamic obstacle updates;
- frontier exploration;
- map versioning;
- incremental refinement.

The agent should discover maps from observations instead of requiring a pre-baked map.

## 6. Planning

GOAP remains appropriate for tactical executable plans, but it should not be the only planner. ReGoap demonstrates the fact/action/goal abstraction. NosAi should add a higher strategic layer and a hierarchical task decomposition layer.

Recommended stack:

```text
Strategic Utility / Goals
        ↓
HTN task decomposition
        ↓
Cost-aware deterministic GOAP
        ↓
Reactive deterministic rules
        ↓
Guard / Trust / Safety
```

The planner must remain bounded and deterministic for identical state/configuration.

## 7. Combat intelligence

A fixed macro is insufficient for the stated target. Combat should maintain an online empirical model keyed by enemy class, build/equipment state, distance/position, resources and observed outcomes.

Candidate action score should combine:

`expected reward - time cost - resource cost - risk + mission value + future positioning value`

The agent should evaluate short action prefixes before committing to longer combos. It should maintain recovery branches and abandon a strategy when observed outcomes diverge from the model.

Learning must modify ranking/model parameters, not bypass Guard/Safety.

## 8. Quest intelligence

Quest understanding should be grounded in client-observable evidence. OCR/semantic extraction produces typed objectives, quantities, entities, prerequisites and rewards. The resulting quest graph is planned by HTN/GOAP.

The LLM is useful for semantic normalization and ambiguity resolution, but cannot directly execute a quest action. Every action must be grounded back to WorldState evidence.

## 9. Memory

Recent memory research strongly supports separating working/episodic/semantic/procedural/reasoning information and using hybrid retrieval rather than a flat vector store. Microsoft's Memora research emphasizes structured abstractions, deduplication and semantic/prompted/hybrid retrieval. Agent Memory for .NET demonstrates persistent long-term facts/entities plus reasoning traces and hybrid vector/full-text/graph retrieval.

NosAi should borrow the concepts, not blindly import the systems. SQLite remains the canonical local persistence layer; graph/vector components should be introduced only where they provide measurable value.

## 10. Simulation and learning

World-model/RL approaches are promising for offline strategy learning, but they should not initially control the live client. The first useful form is a deterministic short-horizon simulator for combat and movement decisions.

Later:

```text
Replay → Dataset → Offline training → Evaluation → Shadow policy
       → A/B comparison → Approval → constrained live ranking
```

A learned policy never becomes an independent execution authority.

## 11. 16 GB RAM / 8 GB VRAM optimization

Hard requirements:

- bounded frame queues;
- pooled image buffers;
- no full-resolution frame history in RAM;
- one primary resident vision model where possible;
- lazy loading of secondary models;
- quantization when accuracy remains acceptable;
- explicit VRAM admission checks;
- background jobs yield under pressure;
- memory consolidation is incremental;
- replay uses compression/retention limits;
- expensive inference has deadlines and cancellation.

## 12. External SSD optimization

The external 2 TB SSD is the canonical storage target. Do not treat it like an internal NVMe device until the actual USB link is benchmarked.

Separate hot and cold paths:

- hot: SQLite WAL, active map cache, current replay window, model cache;
- warm: persistent memory, maps, telemetry;
- cold: datasets, historical replay, research artifacts.

Measure throughput and latency at runtime and store the benchmark with machine metadata.

## 13. New architectural recommendations

Add the following concepts to the implementation backlog:

1. `HardwareCapabilityProfile` — exact CPU/GPU/RAM/driver/SSD/runtime capabilities.
2. `InferenceBudget` — CPU/GPU/VRAM/RAM/thermal/deadline budget per job.
3. `InferenceScheduler` — chooses backend/model tier.
4. `ObservationEnvelope` — provenance/freshness/confidence wrapper.
5. `MapKnowledgeGraph` — persistent spatial/portal/landmark relationships.
6. `CombatExperienceModel` — observed action/outcome statistics.
7. `QuestGraph` — typed objective dependency graph.
8. `CharacterBuildModel` — equipment/stat utility model.
9. `ActionOutcomeLedger` — observation → decision → action → result.
10. `AutonomySession` — complete lifecycle and certification evidence.

## 14. Sources consulted

- NVIDIA GeForce RTX 50 Series laptop specifications: official NVIDIA documentation.
- Microsoft Windows Graphics Capture documentation.
- Microsoft Windows ML / execution-provider documentation.
- DotRecast / Recast Navigation.
- Microsoft Memora.
- Agent Memory for .NET.
- ReGoap.

External research informs implementation decisions but does not override NosAiProject ADRs, the Autonomous Player specification or the non-privileged observation/control boundary.
