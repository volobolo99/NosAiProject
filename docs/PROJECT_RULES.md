# NosAi — Project Rules

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## 1. Version governance

The project version is **1.0 Beta**. No implementation, refactor, test, documentation or automation may change the version unless explicitly requested by the creator.

## 2. Safety-first execution

Safety is fail-closed. Planning, ranking and AI providers cannot bypass Guard AI or the Safety Gate. No component may directly convert an untrusted decision into live execution.

## 3. Layer separation

- Perception observes and produces semantic snapshots.
- World Model owns canonical semantic state.
- Partner and Pet systems remain independently modeled while participating in coordinated planning.
- Coordinated Action Manager proposes coordinated actions; it does not execute them.
- Tactical Ranking ranks candidates; it does not authorize execution.
- Orchestrator coordinates modules; it is not a safety bypass.
- Guard AI evaluates risk, trust and degradation.
- Safety Gate is the final execution authorization boundary.
- Game/client I/O is isolated behind explicit adapters.
- LLMs are decision providers only.

## 4. Deterministic baseline

Every critical decision path must remain testable deterministically without the live game client. Simulation/lookahead is the preferred validation path before live execution.

## 5. Perception boundary

The intended production Perception has seven layers: DXGI capture, lock-free triple buffer, multi-ROI HSV vision, YOLO detection, glyph-hash OCR with AI-OCR fallback/cache, temporal 2D Kalman filtering, and Game State Evaluation. Current repository foundations must not be mislabeled as production backends.

## 6. Bring-up boundary

The first reliable runtime milestone is minimal Play AI + Play Guard + Guard AI bring-up: authenticated local/LAN session, HELLO/CAPABILITIES/HEARTBEAT/STATUS exchange, deterministic reconnect/disconnect and validation without the game client.

## 7. Persistence and learning

Progression Engine and Knowledge Base changes must follow explicit evidence/strategy lifecycle rules. Validated knowledge cannot be silently overwritten by a single execution.

## 8. External implementation points

Specialist-dependent areas remain explicit `EXTERNAL_IMPLEMENTATION_REQUIRED` boundaries where appropriate. The clean project does not silently introduce bypass, anti-cheat evasion, packet manipulation or client injection.

## 9. Legacy repository

`volobolo99/NosAi` is reference-only. Components are audited and selectively reimplemented; no blind copying.

## 10. Documentation integrity

Implementation status must distinguish **implemented**, **foundation**, and **planned**. Documentation must reflect the actual repository state and must not claim production readiness for an unimplemented backend.
