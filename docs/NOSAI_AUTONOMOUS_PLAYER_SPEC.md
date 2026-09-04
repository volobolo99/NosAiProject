# NosAiProject — Autonomous Player Specification

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** Canonical product specification

## 1. Mission

NosAiProject is an autonomous player for the private educational/test environment. The target is full operational autonomy inside the declared scope: perceive the client, build and maintain a world model, understand goals and quests, navigate unknown maps, fight, manage inventory/equipment/progression, recover from failures, learn from experience, and verify every action.

"100% autonomous" means no human gameplay decisions are required during an autonomous run. It does **not** mean omniscience or guaranteed success. When evidence is insufficient, the agent must represent `UNKNOWN`, avoid unsafe assumptions, and pause or recover.

## 2. Non-privileged boundary

The agent may exploit the software and hardware resources available on the PC, but gameplay truth and control must remain within the ordinary-client boundary.

### Allowed

- CPU, GPU/NPU when available, RAM and local storage;
- normal Windows APIs and local processes/threads;
- client-visible network traffic;
- legitimately readable local client process memory;
- pixels/frame capture, OCR and computer vision;
- audio available to the PC when useful;
- local telemetry, replay and NosAi-owned databases;
- software actuation exposed by the supported client/runtime;
- mouse and keyboard as **permitted but optional** input devices.

### Forbidden

- server database access;
- GM/mod/admin commands or panels;
- server console or privileged server APIs;
- hidden/debug/admin gameplay flags;
- secret/admin credentials;
- server-side changes whose purpose is to expose hidden gameplay truth;
- external hardware automation devices or peripherals beyond the permitted mouse/keyboard;
- any information channel unavailable to an ordinary client/player.

Every gameplay fact carries provenance. `Unknown` is never converted into a convenient default.

## 3. Canonical runtime loop

```text
Observe
  -> Sensor Fusion
  -> World Model
  -> Simulation / Prediction
  -> Ranking / Utility
  -> Strategic Orchestrator
  -> HTN / GOAP Planner
  -> Guard
  -> Trust / Authorization
  -> Safety Gate
  -> Execute
  -> Verify
  -> Re-observe
```

LLMs may interpret, summarize, retrieve knowledge and propose plans. They never receive direct execution authority.

## 4. Autonomous subsystems

### 4.1 Perception and sensor fusion

Create a unified observation layer for Network, Memory, Screen and Local telemetry. Each observation includes timestamp, confidence, provenance and freshness.

Required capabilities:

- client/window discovery;
- screen capture and ROI management;
- OCR for quests, NPC dialogue, UI, inventory and notifications;
- object detection/tracking for player, mobs, NPCs, drops and interactables;
- network packet/event interpretation where visible to the client;
- legitimate process-memory readers for stable, validated client state;
- conflict resolution between sensors;
- degradation when a sensor disappears.

### 4.2 World model

The world model is the single semantic state consumed by planning. It contains:

- player state, position and orientation;
- map identity, observed bounds, walkable regions and portals;
- visible and remembered entities;
- mob taxonomy and combat state;
- NPCs, interactables and drops;
- quests and quest progress;
- inventory, equipment, currencies and resources;
- buffs, debuffs, cooldowns and status effects;
- uncertainty and provenance for every important fact.

### 4.3 Mapping and navigation

NosAi must be able to enter an unknown map, explore it, estimate its dimensions, reconstruct walkable geometry and persist the result.

Pipeline:

```text
Observations -> Map Reconstruction -> Geometry -> Walkability
             -> Landmarks/Portals -> NavMesh/Grid -> Hierarchical Pathfinding
             -> Movement -> Verification -> Map Update
```

Use a hierarchical representation. A coarse graph handles long-distance routing; a local navigation layer handles movement around dynamic obstacles. Persist map versions with confidence and provenance.

### 4.4 Exploration

Exploration is an explicit goal planner, not random walking. The agent chooses frontier regions using information gain, travel cost, risk and mission relevance. It must detect completion, dead ends, inaccessible areas and map transitions.

### 4.5 Combat

Combat is a closed-loop decision system:

```text
Observe combat state
 -> generate legal candidates
 -> reject impossible/unsafe actions
 -> predict short-horizon outcomes
 -> score utility/risk
 -> choose action or combo prefix
 -> execute
 -> verify result
 -> update combat model
```

The combat model learns per-enemy and per-build effectiveness from observed outcomes. Candidate scoring should consider DPS, time-to-kill, resource cost, survivability, cooldown alignment, positioning, crowd risk, escape probability and mission objective.

Combo optimization must be adaptive rather than a static macro. The system may maintain opening sequences, continuation policies and recovery branches learned from real observations.

### 4.6 Quest understanding

Quest input is parsed from client-visible text/UI/network evidence into a typed quest graph.

```text
Text/UI evidence
 -> OCR/semantic extraction
 -> entities, quantities, constraints, rewards
 -> Quest Graph
 -> subgoals
 -> planner
 -> actions
 -> verification
```

The quest engine must support travel, dialogue, collection, combat, interaction, delivery, conditional objectives and multi-step dependencies. If an objective cannot be grounded in permitted observations, it remains `UNKNOWN`.

### 4.7 Character, inventory and equipment

The character subsystem continuously evaluates build quality instead of blindly equipping the newest item.

Decision dimensions include:

- effective combat power;
- survivability;
- resource efficiency;
- enemy-specific performance;
- movement/utility;
- upgrade cost;
- opportunity cost;
- quest relevance;
- confidence of the underlying item/stat observations.

Actions include equip, unequip, compare, upgrade when legitimately available, consume, store, sell/discard when permitted, and recover resources.

### 4.8 Strategic autonomy

A hierarchical planner selects the current life-cycle objective, for example:

```text
Survive > recover > complete urgent quest > progress build
       > obtain required item > explore > farm > optimize
```

Priority is contextual and configurable. Strategic goals must be translated into deterministic executable plans before reaching the Safety Gate.

### 4.9 Memory and learning

Memory is divided into working, episodic, semantic, procedural, spatial, combat, quest, character and failure memory.

The agent records:

- what happened;
- what was observed;
- what action was chosen;
- why it was chosen;
- outcome and evidence;
- confidence;
- failures and recovery;
- learned action effectiveness.

Long-term retrieval must be provenance-aware. A remembered fact cannot silently override fresher contradictory observations.

### 4.10 Simulation and prediction

Before consequential actions, the agent may evaluate short-horizon hypothetical outcomes using a local deterministic simulator. Simulation is advisory; it never becomes authoritative gameplay truth.

Future model-based learning (including world-model/RL experiments) remains offline/sandboxed until independently validated and never bypasses Safety.

### 4.11 Recovery

Recovery is autonomous and fail-closed:

- lost client -> reacquire or safe stop;
- stale observation -> stop/refresh;
- contradictory sensors -> reduce confidence and reconcile;
- blocked path -> local replan then global replan;
- failed action -> verify, classify, retry only when policy permits;
- death/disconnect/resource exhaustion -> execute a recovery plan when safely observable;
- repeated failure -> enter safe state and record evidence.

## 5. Execution architecture

Execution adapters are interchangeable. Mouse and keyboard are optional backends, not architectural requirements. Any execution mechanism must pass through one authorization path and the Safety Gate, and must be followed by verification.

```text
PlanStep -> Intent Digest -> Authorization -> Safety Gate -> Effector -> Receipt -> Verification
```

No AI component can invoke an effector directly.

## 6. Quality targets

A subsystem is considered production-ready only after deterministic unit tests, integration tests and real-client evidence where applicable. Performance budgets are measured rather than assumed.

The autonomy certification suite must prove at minimum:

1. clean startup and client attach;
2. map discovery and persistent reconstruction;
3. navigation on known and partially unknown maps;
4. target recognition and combat against representative mob classes;
5. adaptive combat decisions and recovery;
6. quest parsing and multi-step completion;
7. inventory/equipment evaluation and safe changes;
8. persistence across restarts;
9. recovery from missing/stale/conflicting observations;
10. complete evidence chain from observation to decision to action to verification.

## 7. Research and implementation policy

Use the local `third_party/` vault before external code search. Preserve all GPL/LGPL/MIT/Apache/ZLib provenance and license notices; third-party source is reference material unless explicitly integrated and reviewed.

Preferred technical directions:

- ONNX Runtime/DirectML or equivalent local inference for vision;
- OCR and object tracking for client UI/world perception;
- DotRecast-style navmesh/hierarchical navigation;
- deterministic GOAP plus hierarchical task planning;
- provenance-aware hybrid memory/RAG;
- local simulation for action evaluation;
- OpenTelemetry-style traces/metrics/logs for evaluation.

## 8. Definition of autonomy

NosAi reaches autonomous-player certification only when it can run the declared test scenario without hidden gameplay information, without human gameplay commands, and without external automation hardware, while continuously perceiving, planning, acting, verifying, learning and recovering within the permitted boundary.
