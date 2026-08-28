# NosAiProject

Clean-source implementation of **NosAi**, an AI runtime for NosTale.

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

> The version is locked at **1.0 Beta** until the creator explicitly requests a change.

## Current status

The repository is the clean development source. The legacy repository `volobolo99/NosAi` is reference-only: legacy code is audited and selectively reimplemented, never copied blindly.

The runtime now includes a bounded autonomous Agent Runtime foundation: multi-step planning, per-step Guard/Safety/Trust authorization, verification, retry/replanning, resumable checkpoints and an independent watchdog. It remains testable without a live game client.

## Architecture

```text
Session / Scheduler / Resource / Policy
                    ↓
             Provider Router
                    ↓
       Decision Provider (no execution privilege)
                    ↓
Perception → World Model → Party/Pet/Partner
                    ↓
Simulation / Tactical Ranking / Orchestrator
                    ↓
Planner → Guard AI → Trust → Safety Gate
                    ↓
Executor → Verifier
          ↘ Recovery / Replanning
          ↘ Watchdog / Checkpoint
                    ↓
Telemetry / Memory / Knowledge
```

Execution remains behind explicit adapters and safety boundaries. Local-first provider routing, deterministic resource selection, hardware profiling and LAN session protocol foundations are available as independent runtime components.

## Reliability / autonomy rules

- Autonomous execution is bounded by step, retry, replan and watchdog budgets.
- Every action is independently authorized; a model output cannot bypass Guard/Safety.
- Verification failure is evidence for recovery, never implicit success.
- Recovery can retry the current step or request a new plan with failure context.
- Sessions checkpoint progress and can be stopped/resumed in-process.
- The watchdog can only reduce execution and can never grant privileges.
- Production game capture/live execution remain separate gated milestones.

## Bring-up priority

The first reliability milestone remains the minimal Play AI + Play Guard + Guard AI bring-up: local authenticated session, HELLO/CAPABILITIES/HEARTBEAT/STATUS exchange, deterministic reconnect/disconnect and testability without the game client.

## Priorities

1. Safety and fail-closed execution.
2. Deterministic simulation and testability.
3. Stable contracts between perception, decision, memory and execution.
4. Bounded autonomous runtime with recovery before live execution.
5. Complete Guard AI and safe PC/phone bring-up.
6. Local LLM as an isolated decision provider, never a privileged executor.
7. Hardware-specific optimization only after functional correctness is proven.

## Documentation

- `docs/PROJECT_METADATA.md` — authoritative version/creator metadata.
- `docs/IMPLEMENTATION_STATUS.md` — implementation ledger.
- `docs/ARCHITECTURE.md` — runtime boundaries.
- `docs/PROJECT_RULES.md` — project rules.
- `docs/ROADMAP.md` — implementation gates.
- `docs/AGENT_RUNTIME_PLATFORM_V1_BETA.md` — Agent Runtime expansion specification.
- `docs/PROGRESSION_ENGINE_SPEC.md` — Progression Engine specification.
- `docs/WIFI_BRINGUP.md` — local/LAN bring-up specification.
