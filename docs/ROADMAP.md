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

## Phase 1 — Minimal reliable bring-up
- [ ] Start Play AI on PC
- [ ] Start Play Guard on PC
- [ ] Start Guard AI on phone
- [ ] Establish authenticated PC ↔ phone session
- [ ] Exchange HELLO / CAPABILITIES / HEARTBEAT / STATUS
- [ ] Deterministic safe disconnect/reconnect
- [ ] One-command bring-up validation without game client

## Phase 2 — Guard and safe decision runtime
- [ ] Guard AI runtime
- [ ] Trust Tier 1–4 evaluation
- [ ] Guard → Orchestrator integration
- [ ] Guard → Safety Gate integration
- [ ] Provider registry / fallback policy
- [ ] Telemetry contract

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
- [ ] Target-hardware benchmark

## Phase 6 — Integration gate
- [ ] Full CI
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
