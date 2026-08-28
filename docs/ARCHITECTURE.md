# NosAi — Final Architecture & Communication Model

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## 1. Architectural principle

NosAi is a deterministic, contract-driven runtime. LLMs are Decision Providers only. No stochastic model can directly execute tools, game I/O, permissions or safety decisions. The canonical `WorldState` is the current-state source of truth; the event/trace plane records how the system arrived there.

## 2. Final system

```text
                         ┌──────────────────────────────┐
                         │       SESSION / SCHEDULER    │
                         │ checkpoint • resume • stop   │
                         └──────────────┬───────────────┘
                                        │
               ┌────────────────────────▼────────────────────────┐
               │                 RUNTIME CONTROL PLANE            │
               │ Policy • Trust • Resources • Provider Router    │
               │ Memory • Tools • Watchdog • Evaluation           │
               └────────────────────────┬────────────────────────┘
                                        │
                           ┌────────────▼────────────┐
                           │      EVENT / TRACE BUS   │
                           │ correlation • audit      │
                           │ replay • telemetry       │
                           └────────────┬────────────┘
                                        │
Game / external source ───────► PERCEPTION
                                        │
                              PerceptionWorldAdapter
                                        │
                                        ▼
                               CANONICAL WORLDSTATE
                                        │
                    ┌───────────────────┼───────────────────┐
                    ▼                   ▼                   ▼
                  PARTY              PET / PARTNER       MEMORY
                    │                   │                   │
                    └───────────────────┼───────────────────┘
                                        ▼
                                  CANDIDATE ACTIONS
                                        │
                                        ▼
                                  SIMULATION
                                        │
                                        ▼
                                TACTICAL RANKING
                                        │
                                        ▼
                                  ORCHESTRATOR
                                        │
                                        ▼
                              AGENT PLANNER / LOOP
                                        │
                              GuardDecisionContext
                                        │
                                  GUARD AI / POLICY
                                        │
                                  TRUST BOUNDARY
                                        │
                                  SAFETY GATE
                                        │
                              PLAY AI / EXECUTOR
                                        │
                             GAME / PC PLAY GUARD
                                        │
                                        ▼
                                  ACTION RESULT
                                        │
                                        ▼
                                   VERIFIER
                                        │
                                  ┌─────┴─────┐
                                  │           │
                                PASS        FAIL
                                  │           │
                                  ▼           ▼
                              CHECKPOINT   RECOVERY
                                  │           │
                                  │       retry / replan
                                  │           │
                                  └─────┬─────┘
                                        ▼
                                   RE-OBSERVE
                                        │
                                        └──────────► WORLDSTATE
```

## 3. Communication rules

### 3.1 Synchronous critical path

The safety-critical control loop is deterministic and ordered:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Plan → Guard → Trust → Safety → Execute → Verify → Re-observe`.

A failure cannot skip forward. Safety denial terminates the action. Verification failure never becomes implicit success.

### 3.2 Event/trace plane

The event bus is cross-cutting, not a replacement for synchronous contracts. Events carry `event_id`, `session_id`, `run_id`, `task_id`, `parent_event_id`, `timestamp`, `source`, `event_type`, `schema_version` and structured payload. It is used for telemetry, audit, memory/evidence processing and replay.

Recommended event types: `PerceptionObserved`, `WorldStateUpdated`, `SimulationCompleted`, `RankingProduced`, `DecisionCreated`, `PlanCreated`, `GuardEvaluated`, `SafetyEvaluated`, `ActionRequested`, `ActionExecuted`, `ActionVerified`, `VerificationFailed`, `RecoveryStarted`, `ReplanRequested`, `MemoryRead`, `MemoryWritten`, `ProviderSelected`, `ProviderFallback`, `HardwareProfileChanged`, `SessionStarted`, `SessionResumed`, `SessionInterrupted`, `SessionCompleted`.

## 4. WorldState versioning

Every accepted observation produces a new immutable state version. A state records `state_version`, `parent_version`, observation provenance, source and confidence. Simulation records the input state version; verification compares predicted and actual outcomes from consecutive versions.

`WorldState v41 → planned action → observed outcome → WorldState v42`.

This makes prediction accuracy measurable and enables deterministic replay.

## 5. Decision and planning

Simulation and Tactical Ranking never authorize execution. Ranking produces candidates with score, confidence, risk, expected reward and evidence quality. The Orchestrator converts domain results into bounded runtime plans. Planner output remains data until it passes Guard, Trust and Safety.

## 6. Guard / Trust / Safety

Guard evaluates the complete decision context: WorldState, goal, plan, simulation, ranking, action, risk, trust tier, provider, permissions, hardware and relevant evidence. Trust supplies a deterministic ceiling. Safety is the final fail-closed authorization boundary.

Trust tiers: `OBSERVE (0) → SIMULATE (1) → REVERSIBLE (2) → SENSITIVE (3) → CRITICAL (4)`.

## 7. Execution and verification

The Executor/Game Adapter is the only execution boundary. It receives an already authorized action, never raw LLM output. The Verifier receives the action result and a fresh observation. It returns verified/not verified plus structured evidence. Failed verification starts bounded recovery and causes a fresh observation before replanning.

## 8. Recovery and watchdog

Recovery can retry or request a new plan with failure context. It cannot increase trust or permissions. The independent Watchdog limits runtime, actions, consecutive failures and other configured budgets; it can only reduce execution authority.

## 9. Memory and knowledge

Memory is split into raw experience, observations, episodes, hypotheses and verified knowledge. A failed or unverified outcome is not promoted automatically to verified strategy. Evidence should preserve provenance, confidence and supporting event IDs.

## 10. Provider and hardware routing

Provider Router is local-first and policy-controlled. It evaluates privacy/locality, task complexity, latency, available VRAM/RAM, GPU utilization, temperature, energy and recent provider performance. Cloud escalation is never implicit when local-only policy applies.

Hardware Profiler supplies deterministic runtime profiles; hardware optimization is separate from functional correctness.

## 11. Session / PC / phone communication

Initial bring-up remains local/LAN and authenticated. Session protocol uses explicit typed messages and sequence/replay protection. Intended lifecycle: `HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

Play AI, PC Play Guard and phone Guard AI remain separate processes/roles connected through explicit contracts. A disconnected or invalid session fails closed.

## 12. Perception pipeline

Production target: DXGI Direct Capture → lock-free Triple Buffer → multi-ROI HSV vision → YOLO → glyph-hash OCR with AI-OCR fallback/cache → temporal 2D Kalman filtering → Game State Evaluator → immutable semantic WorldState.

These production backends remain gated until independently validated.

## 13. Observability and replay

Every run should be reconstructable from event/trace data. Evaluation records provider selection, decisions, tool calls, policy/safety blocks, action results, verification, recovery, latency and prediction error. Replay must be simulation-first and must not execute live game I/O.

## 14. Final communication matrix

| From | To | Contract / channel | Result |
|---|---|---|---|
| Perception | World Model | `PerceptionWorldAdapter` | versioned WorldState |
| World Model | Simulation | immutable WorldState | predicted outcomes |
| Simulation | Tactical Ranking | SimulationResult | scored candidates |
| Tactical Ranking | Orchestrator | ranked actions | selected domain decision |
| Orchestrator | Planner/Runtime | runtime plan contract | bounded AgentPlan |
| Decision Provider | Runtime | decision data only | candidate/plan data |
| Planner | Guard | GuardDecisionContext | allow/deny evaluation |
| Guard | Trust/Safety | policy contract | authorization state |
| Safety | Executor | explicit authorization | executable action or block |
| Executor | Perception | observation boundary | new WorldState |
| Executor | Verifier | action result | verification evidence |
| Verifier | Recovery | structured failure | retry/replan |
| Runtime | Memory | event/trace contract | experience/evidence |
| Runtime | Evaluation | trace contract | metrics/replay record |
| Hardware | Provider Router | ResourceSnapshot | provider selection |
| PC | Phone Guard | authenticated SessionMessage | status/guard coordination |

## 15. Non-negotiable boundaries

- No LLM direct execution.
- No Tactical Ranking direct execution.
- No Perception direct execution.
- No Recovery permission escalation.
- No Watchdog permission escalation.
- No cloud escalation when policy forbids it.
- No unverified outcome treated as success.
- No live game integration before explicit safety/release gates.

**Version governance:** architecture remains **NosAi 1.0 Beta** until explicitly changed by the creator.
