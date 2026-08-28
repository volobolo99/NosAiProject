# NOS AI — Agent Runtime Platform

**Architecture Expansion:** v1.0 Beta  
**Version:** 1.0 Beta (locked)  
**Creator:** Volodymyr Ryzhuk

## Purpose

This expansion adds a model-agnostic, local-first runtime layer over the existing deterministic NosAi pipeline without granting execution privileges to stochastic Decision Providers. The autonomous runtime is bounded, observable, recoverable and fail-closed.

## Cross-cutting runtime systems

- **SessionManager** — resumable in-process lifecycle and checkpoints.
- **MemoryBus** — bounded runtime events decoupled from durable persistence.
- **ProviderRegistry / ProviderRouter** — model-agnostic local-first provider selection.
- **RoutingPolicy** — privacy, locality, complexity, latency, VRAM and temperature constraints.
- **ResourceManager** — deterministic resource snapshots and gating.
- **ExecutionPolicy** — explicit execution mode and trust-tier policy.
- **AgentLoop** — multi-step Planner → Guard → Safety → Executor → Verifier runtime.
- **RecoveryController** — deterministic recovery events and callbacks.
- **RuntimeWatchdog** — independent runtime/action/failure kill switch.
- **AgentRuntime** — decision facade routing provider output through Guard AI and Safety Gate.

## Autonomous execution contract

The loop treats the plan as untrusted data. Each step is authorized independently. The caller's Trust Tier is an authorization ceiling; a step may require a lower tier but never a higher one. Unknown or malformed trust requirements fail closed.

```text
Goal / Context
      ↓
   Planner
      ↓
 AgentPlan (untrusted)
      ↓
 ┌──────────────────────────────┐
 │ for each step                │
 │   Watchdog                   │
 │   Trust ceiling              │
 │   Guard AI                   │
 │   Safety Gate                │
 │   Executor                   │
 │   Verifier                   │
 │   Checkpoint                 │
 └──────────────┬───────────────┘
                │ failure
                ▼
       bounded retry / recovery
                │
                ▼
          bounded replan
                │
                ▼
            next plan
```

## Recovery semantics

Executor exceptions and verification failures are never treated as success. The runtime may retry the current step within `max_retries_per_step`; after that it may request a new plan with `recovery_reason` and `failed_step_index`, up to `max_replans`. A watchdog trip or exhausted budget terminates the run fail-closed.

## Watchdog

`RuntimeWatchdog` is independent from model output and cannot grant permissions. It enforces maximum runtime, action count and consecutive failures. Once tripped it blocks further execution until an external runtime owner explicitly resets it.

## Session lifecycle

A session records goal, state, checkpoints and lifecycle events. It supports `RUNNING`, `PAUSED`, `RESUMED`, `STOPPED`, `FAILED` and `COMPLETED` states. Durable SQLite persistence and distributed session recovery remain separate gates.

## Security boundary

Decision Providers implement `DecisionProvider` and return decisions/plans only. They do not receive an `ActionExecutor`, Safety Gate bypass, game adapter, or privileged tool handle. Provider output is data until downstream validation succeeds.

## Local-first policy

`PrivacyClass.LOCAL_ONLY` and `PrivacyClass.SENSITIVE` default to local execution. Cloud escalation remains policy-controlled and is not implicitly authorized by the autonomous loop.

## Resource awareness

Resource and hardware profiling remain deterministic abstractions. OS/GPU-specific probes and real benchmark collection are separate implementation work so the autonomous runtime remains testable without specific hardware or the live game client.

## Current production boundary

Implemented now: bounded multi-step planning, independent authorization, verification, checkpointing, retry/replanning, recovery callbacks, watchdog and offline evaluation primitives.

Still gated: production Guard AI integration, authenticated LAN transport, durable SQLite memory, real hardware probes, local llama.cpp/cloud providers, production perception and live game execution.

## Non-goals in this increment

- No live game execution.
- No cloud credentials or implicit network calls.
- No direct LLM execution privileges.
- No production GPU probing.
- No persistent SQLite implementation.
- No version increment.
