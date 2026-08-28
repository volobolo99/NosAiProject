# NOS AI — Final System Architecture, Communication & Data Flow

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk  
**Status:** consolidated architecture after Agent Runtime + closed-loop integration

## 1. Purpose

This document is the canonical engineering map of NOS AI. It explains what each subsystem does, what it receives, what it produces, how it communicates, and where authority is allowed to exist.

## 2. Master architecture

```text
                           SESSION / SCHEDULER
                                  │
                 ┌────────────────┴────────────────┐
                 │                                 │
          RESOURCE MANAGER                    POLICY ENGINE
                 │                                 │
                 └──────────────┬──────────────────┘
                                ▼
                       RUNTIME CONTROL PLANE
             Provider Router • Memory • Tools • Watchdog
                                │
                                ▼
                       EVENT / TRACE PLANE
             events • correlation • audit • telemetry • replay
                                │
       ┌────────────────────────┼─────────────────────────┐
       │                        │                         │
       ▼                        ▼                         ▼
  PERCEPTION              WORLD MODEL                 MEMORY
       │                        │                         │
       │                 WorldState vN                   │
       └──────────────► PerceptionWorldAdapter ◄─────────┘
                                │
                                ▼
                         CANDIDATE ACTIONS
                                │
                                ▼
                           SIMULATION
                                │
                         PredictedOutcome
                                │
                                ▼
                       TACTICAL RANKING
                                │
                   score/confidence/risk/evidence
                                │
                                ▼
                         ORCHESTRATOR
                                │
                                ▼
                        AGENT PLANNER
                                │
                                ▼
                     GUARD DECISION CONTEXT
                                │
                         GUARD AI / POLICY
                                │
                         TRUST BOUNDARY
                                │
                          SAFETY GATE
                                │
                                ▼
                      PLAY AI / EXECUTOR
                                │
                         GAME ADAPTER
                                │
                                ▼
                          ACTION RESULT
                                │
                   ┌────────────┴─────────────┐
                   │                          │
                   ▼                          ▼
                VERIFIER                 EVENT TRACE
                   │
            fresh observation
                   │
                   ▼
             WorldState vN+1
                   │
          ┌────────┴─────────┐
          │                  │
        PASS                FAIL
          │                  │
          ▼                  ▼
     CHECKPOINT          RECOVERY
          │             retry/replan
          │                  │
          └──────────┬───────┘
                     ▼
               NEXT RUN CYCLE
```

## 3. Authority model

| Layer | May decide | May execute | May grant permissions |
|---|---|---|---|
| Perception | observation facts | No | No |
| World Model | state representation | No | No |
| Simulation | predicted outcomes | No | No |
| Tactical Ranking | ranking candidates | No | No |
| Decision Provider / LLM | decision data | No | No |
| Planner | bounded plan data | No | No |
| Guard AI | safety/policy recommendation | No | No |
| Trust Boundary | deterministic ceiling | No | No |
| Safety Gate | final authorization | No | No |
| Executor/Game Adapter | perform authorized action | **Yes** | No |
| Verifier | verify outcome | No | No |
| Recovery | retry/replan request | No | No |
| Watchdog | reduce/stop execution | No | No |
| Event Bus | record/notify | No | No |

## 4. Critical synchronous path

The authoritative control path is synchronous and deterministic:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Planner → Guard → Trust → Safety → Execute → Verify → Re-observe`.

No event subscriber can insert an execution side effect into this path.

## 5. Event / trace plane

The EventBus is cross-cutting and observational. Each `RuntimeEvent` carries:

- `event_id`
- `session_id`
- `run_id`
- `task_id`
- `parent_event_id`
- timestamp
- source
- event type
- schema version
- structured payload

Core event families include perception, world state, simulation, ranking, decisions, planning, guard/safety, actions, verification, recovery, replanning, memory, provider selection/fallback, hardware changes and session lifecycle.

The event plane is used for audit, telemetry, evaluation, memory/evidence processing and replay. It never becomes an alternate execution path.

## 6. WorldState and provenance

`WorldStateStore` maintains the observation sequence. Every accepted observation creates a new version with parent version, observation id, source and confidence.

Conceptually:

`WorldState v41 → Simulation → Action → Observation → WorldState v42`.

Simulation can therefore be evaluated against the actual post-action state. The production persistence layer will later make this durable across process restart.

## 7. Communication contracts

| Producer | Consumer | Communication | Result |
|---|---|---|---|
| Perception | WorldStateStore | PerceptionWorldUpdate | new state version |
| WorldState | Simulation | immutable snapshot | predicted outcome |
| Simulation | Tactical Ranking | SimulationResult | ranked candidates |
| Tactical Ranking | Orchestrator | ranked-action contract | domain decision |
| Orchestrator | Planner | runtime planning contract | bounded AgentPlan |
| Planner | Guard | GuardDecisionContext | policy evaluation |
| Guard | Trust/Safety | authorization contract | allow/deny |
| Safety | Executor | explicit authorization | executable action/block |
| Executor | Verifier | action receipt/result | verification |
| Executor | Perception | observation boundary | new observation |
| Verifier | Recovery | failure evidence | retry/replan |
| Runtime | EventBus | RuntimeEvent | audit/trace fact |
| Runtime | Memory | event/trace | experience/evidence |
| Runtime | Evaluation | trace | metrics/replay |
| ResourceManager | ProviderRouter | ResourceSnapshot | provider choice |
| Session | PC/Phone Guard | authenticated SessionMessage | status/commands |

## 8. Data lifecycle

### Observation

`raw perception → semantic update → validation → WorldStateStore → versioned state`.

### Decision

`WorldState + Goal → Simulation → Ranking → Orchestrator → AgentPlan`.

### Authorization

`AgentPlan → Guard context → Trust ceiling → Safety Gate`.

### Execution

`authorized action → Executor → result receipt → fresh observation`.

### Verification

`predicted outcome + actual WorldState → VerificationResult → evidence`.

### Recovery

`verification failure → bounded retry/replan → fresh ranking → new authorization`.

## 9. Memory and evidence lifecycle

Raw experience must not automatically become trusted knowledge.

`experience → observation → episode → hypothesis → verified evidence → reusable strategy`.

Evidence keeps provenance, confidence and supporting event ids. Failed verification cannot be promoted directly to verified strategy.

## 10. Provider and hardware routing

The Provider Router is local-first and policy-controlled. It consumes:

- privacy/local-only constraints
- task complexity
- latency objectives
- VRAM/RAM availability
- GPU utilization
- queue/load
- temperature
- energy constraints
- recent provider performance

The router may select a local provider or a permitted fallback provider. Cloud escalation is forbidden when policy says local-only.

Hardware discovery and real benchmarks remain gated; current interfaces are deterministic abstractions.

## 11. Session / PC / phone boundary

The intended authenticated lifecycle is:

`HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

PC Play Guard, phone Guard AI and Play AI are separate roles. Invalid, stale or disconnected sessions fail closed.

## 12. Replay and evaluation

Replay must be simulation-first. It may reconstruct a run from event history and WorldState snapshots but must not perform live game I/O.

Evaluation should compare:

- predicted outcome vs actual outcome
- ranking quality
- decision confidence
- safety blocks
- execution success
- recovery rate
- provider latency
- hardware resource use
- overall task success

## 13. External architecture validation

The design intentionally follows current durable-agent runtime patterns: typed runtime events, durable state/read models, stable correlation ids, explicit control planes, replay/evidence and separation between runtime facts, model providers and external tools. citeturn0search0turn0search2turn0search5

Hardware-aware provider routing is also treated as a live control problem rather than a static model-size lookup; current research highlights queue length, KV-cache use, GPU utilization and recent latency as useful routing signals. citeturn0academia24

## 14. Non-negotiable safety boundaries

1. LLMs never execute.
2. Perception never executes.
3. Simulation never executes.
4. Tactical Ranking never executes.
5. Recovery cannot increase Trust.
6. Watchdog cannot increase Trust.
7. Safety denial is terminal for the current action.
8. Verification failure is never success.
9. Event subscribers cannot execute.
10. Live game integration remains behind explicit release gates.

## 15. Current implementation vs production target

Implemented foundations include the autonomous Agent Runtime, closed-loop observation/replanning, typed EventBus, correlation metadata and versioned WorldState observation store. Durable persistence, production replay, PredictionEvaluator, verified-knowledge persistence, authenticated transport, hardware probing, local model adapters and live game integration remain gated milestones.

## 16. Final result

The final functional contract is:

**NOS AI observes the world, builds a canonical state, predicts outcomes, ranks options, plans a bounded action, authorizes it through independent safety boundaries, executes only through the executor boundary, verifies the real result, records the complete trace, updates the world state, and replans when reality differs from the prediction.**

That loop is the core of NOS AI 1.0 Beta.
