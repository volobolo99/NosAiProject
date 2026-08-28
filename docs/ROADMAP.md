# NosAi — Implementation Roadmap

## Gate 0 — Clean foundation
- [x] Create dedicated repository
- [x] Establish repository hygiene
- [x] Establish architecture and migration rules
- [ ] Add core contracts
- [ ] Add test harness and CI

## Gate 1 — Safe deterministic runtime
- [ ] WorldState / Observation contracts
- [ ] Candidate Action / Decision contracts
- [ ] Safety Gate with fail-closed behavior
- [ ] Deterministic simulation loop
- [ ] Telemetry contract

## Gate 2 — Decision providers
- [ ] Rule-based provider
- [ ] Provider registry / fallback policy
- [ ] Local llama.cpp provider
- [ ] Contract and integration tests

## Gate 3 — Perception and memory
- [ ] Read-only vision pipeline
- [ ] OCR abstraction
- [ ] Frame buffer
- [ ] SQLite memory
- [ ] PTS-synchronized telemetry

## Gate 4 — Game boundary
- [ ] Read-only game/client probe
- [ ] Action adapter in simulation first
- [ ] Live adapter behind explicit safety controls

## Gate 5 — Runtime integration
- [ ] End-to-end simulation
- [ ] Local LLM benchmark on target hardware
- [ ] Runtime profiling
- [ ] Full CI gate

## Legacy repository policy
`volobolo99/NosAi` remains a reference source. Nothing is considered migrated until its implementation is verified against the clean architecture and tests.
