# NosAi — Final Architecture & Communication Model

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## 1. Architectural principle

NosAi is a deterministic, contract-driven runtime. LLMs are Decision Providers only. No stochastic model can directly execute tools, game I/O, permissions or safety decisions. The canonical `WorldState` is the current-state source of truth; the EventBus/trace plane records how the system arrived there.

## 2. Final system

```text
SESSION / SCHEDULER
        │
RUNTIME CONTROL PLANE
Policy • Trust • Resources • Provider Router • Memory • Tools • Watchdog • Evaluation
        │
EVENT / TRACE BUS  ← observational only
        │
PERCEPTION
        │
PerceptionWorldAdapter
        │
WORLDSTATE STORE → WorldState(vN) + provenance
        │
Party / Pet / Partner
        │
Simulation → PredictedOutcome
        │
Tactical Ranking → score/confidence/risk/evidence
        │
Orchestrator
        │
Agent Planner / Loop
        │
GuardDecisionContext
        │
Guard AI / Policy
        │
Trust Boundary
        │
Safety Gate
        │
Play AI / Executor / Game Adapter
        │
Action Result
        │
Verifier + fresh observation
        │
WorldState(vN+1)
   ├─ PASS → checkpoint → next cycle
   └─ FAIL → bounded recovery → replan → fresh ranking
```

## 3. Communication rules

### 3.1 Synchronous critical path

The safety-critical control loop is deterministic and ordered:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Plan → Guard → Trust → Safety → Execute → Verify → Re-observe`.

A failure cannot skip forward. Safety denial terminates the action. Verification failure never becomes implicit success.

### 3.2 Event/trace plane

The EventBus is cross-cutting, observational and synchronous in its current in-process implementation. Events carry `event_id`, `session_id`, `run_id`, `task_id`, `parent_event_id`, `timestamp`, `source`, `event_type`, `schema_version` and structured payload. It is used for telemetry, audit, memory/evidence processing and evaluation; durable persistence/replay remains a gated production milestone.

## 4. WorldState versioning

`WorldStateStore` now maintains an in-memory observation sequence with state version, parent version, observation id, source and confidence. Every accepted observation creates a new version. Durable persistence across restart remains a future milestone.

`WorldState v41 → planned action → observed outcome → WorldState v42`.

## 5. Decision and planning

Simulation and Tactical Ranking never authorize execution. Ranking produces candidates with score, confidence, risk, expected reward and evidence quality. The Orchestrator converts domain results into bounded runtime plans. Planner output remains data until it passes Guard, Trust and Safety.

## 6. Guard / Trust / Safety

Guard evaluates the complete decision context. Trust supplies a deterministic ceiling. Safety is the final fail-closed authorization boundary.

Trust tiers: `OBSERVE (0) → SIMULATE (1) → REVERSIBLE (2) → SENSITIVE (3) → CRITICAL (4)`.

## 7. Execution and verification

The Executor/Game Adapter is the only execution boundary. It receives an already authorized action, never raw LLM output. The Verifier receives the action result and a fresh observation. It returns verified/not verified plus structured evidence. Failed verification starts bounded recovery and causes a fresh observation before replanning.

## 8. Recovery and watchdog

Recovery can retry or request a new plan with failure context. It cannot increase trust or permissions. The independent Watchdog limits runtime, actions, consecutive failures and other configured budgets; it can only reduce execution authority.

## 9. Memory and knowledge

Memory is split into raw experience, observations, episodes, hypotheses and verified knowledge. A failed or unverified outcome is not promoted automatically to verified strategy. Evidence should preserve provenance, confidence and supporting event IDs.

## 10. Provider and hardware routing

Provider Router is local-first and policy-controlled. It evaluates privacy/locality, task complexity, latency, available VRAM/RAM, GPU utilization, temperature, energy and recent provider performance. Cloud escalation is never implicit when local-only policy applies.

## 11. Session / PC / phone communication

Initial bring-up remains local/LAN and authenticated. Session protocol uses explicit typed messages and sequence/replay protection. Intended lifecycle: `HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

## 12. Perception pipeline

Production target: DXGI Direct Capture → lock-free Triple Buffer → multi-ROI HSV vision → YOLO → glyph-hash OCR with AI-OCR fallback/cache → temporal 2D Kalman filtering → Game State Evaluator → immutable semantic WorldState.

These production backends remain gated until independently validated.

## 13. Observability and replay

Every run should be reconstructable from event/trace data. Current EventBus provides typed in-process history and run filtering. Production durable event storage and deterministic replay are next gated milestones. Evaluation records provider selection, decisions, tool calls, policy/safety blocks, action results, verification, recovery, latency and prediction error.

## 14. Final communication matrix

| From | To | Contract / channel | Result |
|---|---|---|---|
| Perception | World Model | `PerceptionWorldAdapter` / `PerceptionWorldUpdate` | versioned observation |
| World Model | Simulation | immutable WorldState | predicted outcomes |
| Simulation | Tactical Ranking | SimulationResult | scored candidates |
| Tactical Ranking | Orchestrator | ranked actions | selected domain decision |
| Orchestrator | Planner/Runtime | runtime plan contract | bounded AgentPlan |
| Decision Provider | Runtime | decision data only | candidate/plan data |
| Planner | Guard | GuardDecisionContext | allow/deny evaluation |
| Guard | Trust/Safety | policy contract | authorization state |
| Safety | Executor | explicit authorization | executable action or block |
| Executor | Perception | observation boundary | new observation |
| Executor | Verifier | action result | verification evidence |
| Verifier | Recovery | structured failure | retry/replan |
| Runtime | EventBus | `RuntimeEvent` | audit/trace fact |
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
- Event subscribers do not acquire execution authority.

**Version governance:** architecture remains **NosAi 1.0 Beta** until explicitly changed by the creator.

See `docs/FINAL_SYSTEM_ARCHITECTURE.md` for the complete end-to-end map, authority model, data lifecycle and communication matrix.
