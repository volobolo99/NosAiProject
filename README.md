# NosAiProject

Clean-source implementation of **NosAi**, an AI runtime for NosTale.

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

> The version is locked at **1.0 Beta** until the creator explicitly requests a change.

## Current status

The repository is the clean development source. The legacy repository `volobolo99/NosAi` is reference-only: legacy code is audited and selectively reimplemented, never copied blindly.

The runtime now provides a bounded autonomous Agent Runtime and a closed-loop domain bridge: observe → orchestrate → guard/safety → execute → verify → re-observe → bounded replan. It remains testable without a live game client.

## Final architecture

```text
Session / Scheduler / Resources / Policy / Providers
                         │
                    Event / Trace Bus
                         │
Perception → WorldState(vN) → Simulation → Tactical Ranking
                         │                         │
                         └──── Party/Pet/Partner ┘
                                           │
                                      Orchestrator
                                           │
                                      Agent Planner
                                           │
                                   Guard / Trust / Safety
                                           │
                                  Play AI / Executor
                                           │
                                        Verifier
                                           │
                              Recovery / Replan / Watchdog
                                           │
                                      Re-observe
                                           └────→ WorldState(vN+1)
```

WorldState is the current-state source of truth. The event/trace plane records provenance, decisions, safety checks, outcomes, recovery and evaluation so runs can be audited and replayed without giving the event system execution authority.

## Communication model

- Perception → World Model through an explicit `PerceptionWorldAdapter`.
- World Model → Simulation through immutable WorldState snapshots.
- Simulation → Tactical Ranking through deterministic SimulationResult data.
- Tactical Ranking → Orchestrator through ranked action contracts.
- Orchestrator → Agent Runtime through bounded AgentPlan contracts.
- Decision Providers supply decision data only; they never execute.
- Guard, Trust and Safety are mandatory authorization boundaries.
- Executor/Game Adapter is the only execution boundary.
- Verifier consumes execution results plus a fresh observation.
- Recovery can retry/replan but cannot escalate permissions.
- Provider Router consumes hardware/resource telemetry and policy constraints.
- Memory and Evaluation consume structured events/traces rather than controlling execution.

## Reliability / autonomy rules

- Autonomous execution is bounded by step, retry, replan and watchdog budgets.
- Every action is independently authorized; model output cannot bypass Guard/Safety.
- Verification failure is evidence for recovery, never implicit success.
- Every successful observation advances the canonical WorldState version.
- Prediction can be compared with actual post-action state.
- Sessions checkpoint progress and can be stopped/resumed in-process.
- The watchdog can only reduce execution and can never grant privileges.
- Production game capture/live execution remain separate gated milestones.

## Priorities

1. Safety and fail-closed execution.
2. Deterministic simulation and testability.
3. Stable contracts and versioned WorldState.
4. Closed-loop autonomy with verification and bounded recovery.
5. Unified event/trace/replay observability.
6. Complete Guard AI and safe PC/phone bring-up.
7. Local LLM as an isolated decision provider, never a privileged executor.
8. Hardware-aware optimization after functional correctness.

## Documentation

- `docs/PROJECT_METADATA.md` — authoritative version/creator metadata.
- `docs/IMPLEMENTATION_STATUS.md` — implementation ledger.
- `docs/ARCHITECTURE.md` — final architecture and communication matrix.
- `docs/PROJECT_RULES.md` — project rules.
- `docs/ROADMAP.md` — implementation gates.
- `docs/AGENT_RUNTIME_PLATFORM_V1_BETA.md` — Agent Runtime expansion specification.
- `docs/PROGRESSION_ENGINE_SPEC.md` — Progression Engine specification.
- `docs/WIFI_BRINGUP.md` — local/LAN bring-up specification.
