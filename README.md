# NosAiProject

Clean-source implementation of **NosAi**, an AI runtime for NosTale.

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

> The version is locked at **1.0 Beta** until the creator explicitly requests a change.

## Current status

The repository is the clean development source. The legacy repository `volobolo99/NosAi` is reference-only: legacy code is audited and selectively reimplemented, never copied blindly.

The project currently contains the core contracts, World Model foundation, Party/Pet/Partner coordination, Tactical Ranking/Simulation foundations, Perception foundations and a Guard/bring-up area. Production game capture, live execution, complete Guard AI, persistence, production perception backends and full runtime integration remain gated work.

## Architecture

```text
Perception
    ↓
World Model
    ↓
Party / Pet / Partner Coordination
    ↓
Simulation / Lookahead
    ↓
Tactical Ranking
    ↓
Orchestrator
    ↓
Guard AI
    ↓
Safety Gate
    ↓
Play AI / Adapter
    ↓
Telemetry / Memory / Knowledge
```

Perception communicates with the canonical WorldState through an explicit adapter. Decision, Guard and execution layers remain separated by contracts.

## Bring-up priority

The first reliability milestone is the minimal Play AI + Play Guard + Guard AI bring-up: local authenticated session, HELLO/CAPABILITIES/HEARTBEAT/STATUS exchange, deterministic reconnect/disconnect and testability without the game client.

## Priorities

1. Safety and fail-closed execution.
2. Deterministic simulation and testability.
3. Stable contracts between perception, decision, memory and execution.
4. Complete Guard AI and safe bring-up before live execution.
5. Local LLM as an isolated decision provider, never a privileged executor.
6. Hardware-specific optimization only after functional correctness is proven.

## Documentation

- `docs/PROJECT_METADATA.md` — authoritative version/creator metadata.
- `docs/IMPLEMENTATION_STATUS.md` — implementation ledger.
- `docs/ARCHITECTURE.md` — runtime boundaries.
- `docs/PROJECT_RULES.md` — project rules.
- `docs/ROADMAP.md` — implementation gates.
- `docs/PROGRESSION_ENGINE_SPEC.md` — Progression Engine specification.
- `docs/WIFI_BRINGUP.md` — local/LAN bring-up specification.
