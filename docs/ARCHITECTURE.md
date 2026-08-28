# NosAi — Architecture

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## Runtime architecture

NosAi remains contract-driven and deterministic at every security boundary. The Agent Runtime is a transverse control plane; it does not replace the original decision pipeline.

```text
                         Runtime Control Plane
 Session / Scheduler / Memory / Resources / Policy / Provider Router
                                │
                                ▼
Game / external sources → Perception → WorldState / World Model
                                │
                    Party + Partner + Pet
                                │
                    Candidate Actions / Simulation
                                │
                       Tactical Ranking
                                │
                         Orchestrator
                                │
             Planner → Guard AI → Trust Boundary
                                │
                          Safety Gate
                                │
                    Executor / Game Adapter
                                │
                          Verifier
                         ↙           ↘
                 Recovery/Replan     Telemetry/Memory
```

## Autonomous Agent Loop

The runtime supports bounded multi-step execution:

1. Planner creates an `AgentPlan`.
2. Each step is checked against the caller's Trust Tier ceiling.
3. Guard and Safety must both approve before execution.
4. Executor performs the step; it is never exposed to the Decision Provider.
5. Verifier validates the observed result.
6. A successful step is checkpointed and the loop advances.
7. Execution errors and verification failures are bounded retry/replan inputs.
8. Replanning receives structured failure context.
9. RuntimeWatchdog independently limits total actions, runtime and consecutive failures.
10. Exhausted budgets fail closed.

## Trust model

`OBSERVE (0) → SIMULATE (1) → REVERSIBLE (2) → SENSITIVE (3) → CRITICAL (4)`.

The runtime caller supplies an authorization ceiling. A plan step may require a lower tier, but it can never exceed the caller ceiling or the configured TrustPolicy. Unknown/invalid step tiers are treated as `CRITICAL` and therefore fail closed under normal policy.

## Session lifecycle

Sessions are observable and resumable in-process. Runtime checkpoints record step index, status and recovery reason. Lifecycle states include `CREATED`, `RUNNING`, `PAUSED`, `RESUMED`, `STOPPED`, `FAILED` and `COMPLETED`. Durable persistence remains a separate implementation gate.

## Recovery

Recovery is deterministic and bounded. The runtime may retry a failed step, invoke a recovery callback, or request a fresh plan carrying `recovery_reason` and `failed_step_index`. Recovery never grants authorization.

## Watchdog

`RuntimeWatchdog` is independent from model output. It enforces runtime, action-count and consecutive-failure budgets. A tripped watchdog is a kill condition and cannot be reset by an agent decision.

## Separation rules

- Perception observes and produces semantic snapshots; it does not execute actions.
- `PerceptionWorldAdapter` is the explicit boundary into canonical `WorldState`.
- World Model is the shared semantic state consumed by decision systems.
- Partner and Pet are coordinated actors but remain independently modeled.
- Coordinated Action Manager proposes coordinated actions; execution belongs downstream.
- Tactical Ranking evaluates candidates and may use deterministic lookahead.
- Orchestrator coordinates modules; it is not a bypass around Guard/Safety.
- Guard AI independently evaluates risk, trust and degradation before execution.
- Safety Gate is fail-closed and remains the final execution authorization boundary.
- Game-specific I/O is isolated behind adapters.
- LLM providers are decision providers only and never privileged executors.
- The watchdog can reduce execution but cannot grant permissions.

## Perception architecture

The intended production Perception follows seven layers:

1. DXGI Direct Capture.
2. Lock-free Triple Buffer.
3. Multi-ROI HSV vision.
4. YOLO object detection.
5. Glyph-hash OCR with AI-OCR fallback/cache.
6. Temporal 2D Kalman filtering.
7. Game State Evaluator producing immutable semantic state.

Production-specific capture/detection/OCR/Kalman backends remain gated.

## Version governance

This architecture is for **NosAi 1.0 Beta**. No implementation or documentation task may silently change the project version.
