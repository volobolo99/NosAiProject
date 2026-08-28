# NosAi — Architecture

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## Runtime boundaries

NosAi is organized as a contract-driven pipeline. Observation, world state, planning, ranking, guarding and execution are independent boundaries.

```text
Game / external sources
        ↓
Perception
  ├─ DXGI capture (pending)
  ├─ ROI / HSV
  ├─ YOLO (pending)
  ├─ OCR (pending)
  └─ temporal tracking (foundation; Kalman pending)
        ↓
Game State Evaluator
        ↓
PerceptionWorldAdapter
        ↓
Canonical WorldState / World Model
        ↓
Party + Partner + Pet coordination
        ↓
Candidate Actions
        ↓
Simulation / Lookahead
        ↓
Tactical Action Ranking
        ↓
Orchestrator
        ↓
Guard AI / Trust Tier evaluation
        ↓
Safety Gate
        ↓
Play AI / Humanizer / Game Adapter
        ↓
Telemetry / Memory / Knowledge Base
```

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

## Perception architecture

The intended production Perception follows seven layers:

1. DXGI Direct Capture.
2. Lock-free Triple Buffer.
3. Multi-ROI HSV vision.
4. YOLO object detection.
5. Glyph-hash OCR with AI-OCR fallback/cache.
6. Temporal 2D Kalman filtering.
7. Game State Evaluator producing immutable semantic state.

The repository currently contains reusable contracts/pipeline plus ROI, tracking and evaluator foundations. Production-specific capture/detection/OCR/Kalman backends remain gated.

## Reliability-first bring-up

Before live game integration, the system must support a minimal Play AI + Play Guard + Guard AI session using local/LAN communication, explicit authentication/capabilities, heartbeat/status and deterministic reconnect/disconnect. The path must be testable without the game client.

## Version governance

This architecture is for **NosAi 1.0 Beta**. No implementation or documentation task may silently change the project version.
