# NosAi AI Capability Deep Research — 2026-09-04

## Scope

Second-pass research focused on advanced capabilities that can materially improve NosAi: cognitive memory, planning, navigation, perception, recovery, anomaly detection, observability, evaluation, and offline learning.

The research is filtered through ADR-0021 and the unprivileged demo specification. No privileged server state, GM/admin controls, hidden gameplay facts, anti-cheat bypasses, or direct learned-model execution authority are acceptable.

## High-value findings

### 1. Cognitive memory

**microsoft/Memora**
- MIT.
- Memory lifecycle with ingestion, abstraction, cue anchors, deduplication, merging and updates.
- Supports semantic, prompted and hybrid retrieval.
- Distinguishes rich stored values from indexed representations.
- Experimental GRPO retrieval policy provides a research path for learned retrieval without granting action authority.

**joslat/agent-memory-dotnet**
- MIT.
- Native .NET graph memory with short-term, long-term and reasoning-trace layers.
- Vector, full-text, hybrid and graph traversal retrieval.
- Bitemporal recall, ownership/scoping, access audit and non-destructive invalidation.

**NosAi adoption:** build our own small provenance-aware memory abstraction first; use these projects as design references rather than coupling the critical path to a specific database.

### 2. GOAP and utility planning

**luxkun/ReGoap**
- Apache-2.0.
- C# GOAP with agent, goal, action, memory and sensor abstractions.
- A* planning and replanning around world-state changes.
- Comparator conditions support count/threshold style goals.

**caesuric/mountain-goap**
- MIT.
- Generic C# GOAP with composition-oriented agents, weighted goals, comparative goals/preconditions, arithmetic postconditions, permutation selectors, cost callbacks, state mutators and state checkers.

**NosAi adoption:** create `IPlanner` adapters and benchmark GOAP against a native deterministic planner. Planner output remains candidate plans only.

### 3. Navigation

**ikpil/DotRecast**
- ZLib.
- C# Recast/Detour port.
- Navmesh generation, runtime queries/pathfinding, tiled navmesh streaming, crowd simulation and dynamic navmesh support.

**NosAi adoption:** use for deterministic movement feasibility and route generation where a navigable map can be reconstructed from ordinary-client observations. Never inject privileged map state.

### 4. Agent orchestration and recovery

**Continuum** (search result: shyftlabs/continuum)
- Provides workflow-agent patterns including Router, Sequential, Parallel, Loop, Reflection, Planner, Debate, Scatter and supervised workflows.
- Includes memory, session, observability and evaluation areas and optional durable workflows with human-in-the-loop gates.

**KingshotAuto / similar game automation projects**
- Useful patterns for task state machines, OCR/CV loops, retry, startup recovery and multi-instance scheduling.
- Use as pattern mining only; do not import anti-cheat bypass or privileged mechanisms.

**NosAi adoption:** formalize `Disconnected -> Attaching -> Observing -> Ready -> Acting -> Verifying -> Recovering -> SafeStop` with explicit timeout/cancellation and journaled transitions.

### 5. Observability and model monitoring

**Scouter** (search result: demml/scouter)
- Developer-first monitoring/observability for AI workflows.
- Drift detection using PSI/SPC/custom metrics.
- Low-overhead queue and tracing concepts.

**AgentOps**
- Useful agent tracing and operational telemetry patterns.

**NosAi adoption:** add structured traces for observation -> world state -> plan -> guard -> safety -> execution -> verification. Add drift metrics for perception confidence and model behavior.

### 6. Evaluation

**OpenAI Evals** and the broader agent-evaluation ecosystem are useful as conceptual references for task suites, graders, transcripts and repeatable evaluation harnesses.

**NosAi adoption:** create deterministic replay/evaluation suites with:
- known observation streams;
- expected uncertainty;
- expected candidate plans;
- Safety Gate decisions;
- execution verification;
- recovery behavior;
- latency and allocation budgets.

### 7. RL / self-play / imperfect information

The game-AI research ecosystem contains mature work on self-play, imperfect-information learning, multi-agent RL, AlphaZero-style search and policy optimization.

**NosAi adoption:** RL is an offline research component only in the first certified runtime. Train/evaluate on replay or simulation, then export a candidate policy/ranking artifact. Promotion requires regression tests, deterministic validation and Safety Gate compatibility.

## New architecture opportunities

### A. Provenance-aware memory graph

Each memory item should carry:
- `MemoryId`;
- `MemoryType` = Working/Episodic/Semantic/Procedural/Reasoning;
- `Provenance`;
- `Confidence`;
- `ObservedAt` and `RecordedAt`;
- session/run identifiers;
- evidence references;
- invalidation/supersession metadata.

### B. Hybrid retrieval score

Candidate ranking can combine:
- exact/keyword match;
- vector similarity;
- recency;
- task relevance;
- provenance confidence;
- prior utility/verification success.

A deterministic scorer should be used on the critical path. Learned reranking can remain advisory/offline initially.

### C. Planner stack

Use different planning methods for different problems:

- Behavior Tree / finite-state routine: predictable routine.
- GOAP: dynamic action sequencing.
- HTN: hierarchical multi-step procedures.
- Navigation planner: spatial route.
- RL policy: offline optimization candidate.

All converge on:
`CandidatePlan -> deterministic ranking -> Guard -> Safety Gate -> Execute -> Verify`.

### D. Recovery-first design

Every capability must define:
- timeout;
- cancellation;
- stale-observation handling;
- conflicting-sensor handling;
- disconnect handling;
- retry budget;
- safe-stop behavior;
- journal evidence.

### E. Evaluation as a first-class subsystem

Every new AI capability should ship with an evaluation adapter before production integration.

Minimum metrics:
- task success rate;
- false positive/negative perception rate;
- plan validity rate;
- replanning rate;
- recovery success rate;
- p50/p95/p99 latency;
- allocations on critical path;
- safety rejection rate;
- provenance violations = 0.

## Candidates to add to the local vault next

Priority 1:
- ReGoap core planner interfaces and selected planner implementation.
- Agent Memory for .NET memory contracts and security/architecture docs.
- Memora memory representation/retrieval components.
- DotRecast pathfinding tests and minimal Detour interfaces.

Priority 2:
- evaluation harness patterns;
- tracing/observability patterns;
- recovery state-machine patterns;
- OCR/CV perception adapters.

Priority 3:
- RL/self-play research implementations for offline simulation only.

## Rejection criteria

Reject or isolate any candidate that:
- requires server DB/admin/GM access;
- uses hidden state as gameplay truth;
- injects into the game in a way outside the declared client boundary;
- bypasses security/anti-cheat controls;
- grants an LLM/RL model direct execution authority;
- cannot preserve provenance;
- cannot be tested deterministically on replay/simulation;
- introduces an unbounded failure mode without safe-stop.

## Immediate implementation order

1. `NosAi.Memory` contracts and SQLite/replay implementation.
2. Provenance-aware hybrid retrieval.
3. GOAP adapter behind `IPlanner`.
4. Navigation adapter with DotRecast isolated from action authority.
5. Recovery state machine integrated with watchdog.
6. Observation/decision/action tracing.
7. Deterministic evaluation/replay harness.
8. ONNX/OCR perception adapters.
9. Offline policy learning/self-play.
10. Controlled promotion pipeline for learned artifacts.
