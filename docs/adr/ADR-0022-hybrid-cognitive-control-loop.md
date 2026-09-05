# ADR-0022 — Hybrid cognitive control loop

**Status:** Accepted  
**Date:** 2026-09-05  
**Scope:** Autonomous Player cognition and learning

## Decision

NosAi adopts a multi-timescale hybrid cognitive architecture instead of a single monolithic AI loop.

The runtime separates:

1. **Reflex loop** — deterministic, low-latency safety/recovery reactions.
2. **Tactical loop** — target, combat, local movement and short-horizon decisions.
3. **Strategic loop** — quests, progression, exploration, resources and long-horizon planning.
4. **Reflective loop** — asynchronous memory consolidation, outcome analysis and offline learning.

All four loops consume the canonical WorldState and remain subordinate to the existing:

`Guard → Trust → Safety → Execute → Verify`

execution chain.

## Cognitive data flow

```text
Network / Memory / Screen / Local
            ↓
      Temporal Fusion
            ↓
       Belief State
            ↓
        World Model
       ↙     ↓      ↘
 Working  Episodic  Semantic
 Memory   Memory    Knowledge
       ↘     ↓      ↙
     Procedural Skill Library
            ↓
   Attention / Observation Scheduler
            ↓
   Prediction + Utility / Risk
            ↓
 HTN / GOAP / Reactive Controller
            ↓
 Candidate Plan / Action Intent
            ↓
 Guard → Trust → Safety
            ↓
 Execute → Verify → Re-observe
            ↓
 Outcome → Reflection → Memory update
```

## Rules

### 1. Temporal belief state

Planning must not depend exclusively on the latest observation. A bounded temporal window should derive motion, trends, action progress, confidence trends and sensor disagreement.

### 2. Attention is scheduled

Perception resources are allocated according to uncertainty and decision impact. The runtime should prefer ROI/event-driven observations over unnecessary full-frame or expensive inference.

### 3. Prediction is advisory

Short-horizon simulation/model prediction may rank candidates but cannot redefine authoritative gameplay truth. Contradictory predicted state remains predicted/unknown until observed.

### 4. Memory is typed

Working, episodic, semantic, procedural, spatial and reflective knowledge have different retention and retrieval policies. Fresh permitted observations outrank stale memory.

### 5. Learning is conservative

The certified runtime initially uses observed-outcome statistics for adaptation. Online RL is not execution-authoritative. Learned policies/rankers require offline evaluation, regression tests and controlled promotion.

### 6. Incremental replanning

When a world-state change invalidates only the suffix of a plan, the runtime should reuse the valid prefix and replan the smallest affected region. This is an optimization only; safety and verification remain unchanged.

### 7. Skill library

Reusable procedural skills may be learned or proposed, but each skill must declare preconditions, expected observations, timeout, abort conditions, evidence and confidence. Skills are candidate procedures, not privileged execution paths.

### 8. Uncertainty is explicit

Low confidence, stale observations and sensor disagreement cause re-observation, alternative sensing, conservative ranking, replan or safe-stop. They never silently become truth.

## Performance policy

- Reflex work must not wait on LLM/ML inference.
- Tactical decisions use bounded deadlines.
- Strategic reasoning is interruptible.
- Reflective work is background/preemptible.
- Queues are bounded.
- Critical paths avoid avoidable allocations.
- GPU inference uses adaptive tiers and respects detected hardware limits.

## Evaluation requirements

Every cognitive capability must expose measurements for:

- decision latency p50/p95/p99;
- plan validity;
- goal success;
- unnecessary actions;
- replan frequency;
- stale-state duration;
- confidence calibration;
- sensor disagreement;
- repeated failures;
- memory reuse;
- recovery success;
- Safety rejection;
- forbidden-source violations.

## Security and scope

This ADR does not expand the information boundary. All observations remain constrained by ADR-0021. No privileged server data, GM/admin tools, hidden state, secret credentials or external automation hardware may be introduced to improve cognition.

## Consequence

NosAi can become faster and more intelligent by improving information flow and scheduling rather than increasing model size or inference frequency. The architecture can progressively add local ML, world-model learning, search and LLM reasoning without granting those components direct gameplay authority.
