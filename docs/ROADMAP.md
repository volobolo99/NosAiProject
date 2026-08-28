# NosAi — Implementation Roadmap

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

> Version remains 1.0 Beta until explicitly changed by the creator.

## Phase 0 — Clean foundation
- [x] Dedicated repository
- [x] Repository hygiene
- [x] Architecture and migration rules
- [x] Deterministic decision baseline
- [x] Safety Gate boundary
- [x] Transport-independent Guard protocol
- [x] Core WorldState / Goal / Action / Decision contracts
- [x] World Model foundation
- [x] Partner / Pet coordination foundations
- [x] Coordinated Action Manager foundation
- [x] Tactical Ranking + deterministic simulation foundation
- [x] Perception contracts/pipeline
- [x] Perception → WorldState adapter
- [x] Agent Runtime contracts, provider routing, resources and policy
- [x] Bounded multi-step autonomous loop with verification
- [x] Retry/replan recovery and independent watchdog

## Phase 1 — Minimal reliable bring-up
- [ ] Start Play AI on PC
- [ ] Start Play Guard on PC
- [ ] Start Guard AI on phone
- [ ] Establish authenticated PC ↔ phone session
- [ ] Exchange HELLO / CAPABILITIES / HEARTBEAT / STATUS
- [ ] Deterministic safe disconnect/reconnect
- [ ] One-command bring-up validation without game client

## Phase 2 — Guard and safe decision runtime
- [x] Guard AI runtime foundation
- [x] Trust Tier 1–4 policy boundary
- [x] Guard/Safety boundary integration in autonomous loop
- [x] Provider registry / local-first fallback policy
- [x] Runtime evaluation trace foundation
- [ ] Production Guard AI runtime across PC/phone
- [ ] Production watchdog/recovery state propagation
- [ ] Telemetry contract integration

## Phase 3 — Production perception and memory
- [x] ROI vision foundation
- [x] Temporal tracking foundation
- [x] Game State Evaluator foundation
- [ ] DXGI Direct Capture
- [ ] Lock-free Triple Buffer
- [ ] Production YOLO detector
- [ ] Glyph-hash OCR + AI-OCR fallback/cache
- [ ] Production 2D Kalman tracking
- [ ] Complete game-specific semantic evaluator
- [ ] SQLite memory
- [ ] PTS-synchronized telemetry
- [ ] Deterministic anomaly detection/recovery

## Phase 4 — Game boundary
- [ ] Read-only game/client probe
- [ ] Simulation-first action adapter
- [ ] Controlled live adapter behind Guard/Safety

## Phase 5 — Strategy and AI providers
- [ ] Progression Engine V2
- [ ] MAUT / UCB1 / HTN-MCTS
- [ ] Beta-Binomial evidence updates
- [ ] Strategy lifecycle + mastery persistence
- [ ] Knowledge Base
- [ ] Local llama.cpp provider
- [ ] Cloud provider adapters with policy-controlled escalation
- [ ] Target-hardware benchmark and automatic runtime profiles

## Phase 6 — Integration gate
- [x] CI includes Python tests/compile validation and C# runtime build validation
- [ ] End-to-end deterministic tests
- [ ] Runtime integration tests
- [ ] Hardware benchmark gate
- [ ] Release readiness review

## External implementation points
These remain explicit placeholders and are not silently converted into implementation claims:
- `EXTERNAL_IMPLEMENTATION_REQUIRED: game-client-specific integration`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: anti-cheat compatibility/research`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: packet/network integration`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: client-specific bypass/injection work`

The clean project does not implement bypass, anti-cheat evasion, packet manipulation or client injection as part of the minimal bring-up.

## Legacy repository policy
`volobolo99/NosAi` remains reference-only. A component is considered migrated only after architectural review, selective reimplementation and tests.
