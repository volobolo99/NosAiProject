# NOS AI — Agent Runtime Platform

**Architecture Expansion:** v1.0 Beta  
**Version:** 1.0 Beta (locked)  
**Creator:** Volodymyr Ryzhuk

## Purpose

This expansion adds a model-agnostic, local-first runtime layer over the deterministic NosAi pipeline without granting execution privileges to stochastic Decision Providers. The runtime is bounded, observable, recoverable and fail-closed.

## Runtime control plane

SessionManager, Scheduler, Memory, Policy, Trust, Resources, ProviderRouter, Tools, Watchdog and Evaluation operate as a transverse control plane. They govern execution but do not replace the canonical domain pipeline.

## Closed-loop contract

```text
Observe
  ↓
Canonical WorldState(vN)
  ↓
Simulation → Tactical Ranking → Orchestrator
  ↓
Planner → GuardDecisionContext → Trust → Safety
  ↓
Executor / Game Adapter
  ↓
ActionResult
  ↓
Verifier + fresh Observation
  ↓
Canonical WorldState(vN+1)
  ├── verified → checkpoint → next decision
  └── failed → bounded retry/recovery → fresh replan
```

Every action is independently authorized. The caller Trust Tier is a ceiling. Unknown or malformed trust requirements fail closed. Recovery and watchdogs can only reduce authority.

## Event / trace plane

The runtime should emit typed events without making the event bus an execution path. Events carry `event_id`, `session_id`, `run_id`, `task_id`, `parent_event_id`, timestamp, source, type, schema version and payload.

Core event families include perception, WorldState updates, simulation, ranking, decisions, plans, Guard/Safety evaluations, execution, verification, recovery, replanning, memory, provider routing, hardware profile changes and session lifecycle.

This plane supports audit, telemetry, evaluation and simulation-first replay.

## WorldState provenance

The canonical WorldState is immutable per accepted observation. Each version identifies its parent and observation provenance. Simulation references the exact input state version. Verification compares predicted and actual outcomes after the next observation.

This produces the measurable chain:

`WorldState vN → prediction → action → WorldState vN+1 → prediction error`.

## Decision / ranking semantics

Decision Providers return data only. Simulation predicts. Tactical Ranking scores candidates but never authorizes. The Orchestrator coordinates. The Planner creates bounded plans. Guard evaluates contextual risk. Trust supplies deterministic authorization ceilings. Safety is the final fail-closed gate.

Ranking should expose score, confidence, risk, expected reward, prediction confidence and evidence quality so decisions can be audited and compared over time.

## Memory semantics

Runtime memory distinguishes raw experience, observation, episode, hypothesis and verified knowledge. Verification evidence and provenance are required before an experience is promoted to reusable strategy. Unverified outcomes cannot silently become knowledge.

## Provider / hardware routing

Provider Router is local-first and policy-controlled. Inputs include privacy/locality, task complexity, latency, VRAM/RAM, GPU utilization, temperature, energy and recent provider performance. Hardware profiling remains deterministic at the contract layer; real probes and benchmarks are gated.

## Recovery / watchdog

Executor exceptions and verification failures are never success. The runtime may retry within budget and then replan with structured failure context. The independent watchdog limits runtime, actions, consecutive failures and other configured budgets. A tripped watchdog cannot be reset by model output.

## Session / PC / phone

Initial bring-up is local/LAN and authenticated. Typed messages use sequence/replay protection. Intended lifecycle: `HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`. PC Play AI, PC Play Guard and phone Guard AI remain separate roles connected through explicit contracts; invalid/disconnected sessions fail closed.

## Security invariants

- No LLM direct execution.
- No ranking/orchestrator direct execution.
- No perception direct execution.
- No recovery permission escalation.
- No watchdog permission escalation.
- No cloud escalation when local-only policy applies.
- No unverified outcome treated as success.
- No live game integration before release/safety gates.

## Current production boundary

Implemented: bounded autonomous runtime, closed-loop observation/replanning bridge, deterministic Trust/Guard/Safety boundary, provider/resource foundations, session/checkpoint foundations and evaluation primitives.

Gated: production Event Bus, persistent/versioned WorldState store, PredictionEvaluator, evidence-aware knowledge persistence, authenticated LAN transport, production Guard/Play Guard, hardware probes, local/cloud providers, production perception and live game adapter.

**No version increment:** project remains **NosAi 1.0 Beta**.
