# NosAi — Implementation Roadmap

## Project priority: Play AI + Play Guard minimal bring-up
Before expanding AI features, the project must prove the smallest reliable startup path:
- [x] Create dedicated repository
- [x] Establish repository hygiene
- [x] Establish architecture and migration rules
- [x] Define deterministic decision baseline
- [x] Define Safety Gate boundary
- [x] Define transport-independent PC Guard ↔ phone Guard AI protocol
- [ ] Start Play AI on PC
- [ ] Start Play Guard on PC
- [ ] Start Guard AI on phone
- [ ] Establish PC ↔ phone session
- [ ] Exchange HELLO / CAPABILITIES / HEARTBEAT / STATUS
- [ ] Safe disconnect + reconnect behavior
- [ ] One-command minimal bring-up validation

## Gate 1 — Safe deterministic runtime
- [x] WorldState / Goal / Action / Decision contracts
- [x] Safety Gate boundary
- [x] Deterministic provider baseline
- [ ] Provider registry / fallback policy
- [ ] Telemetry contract

## Gate 2 — Decision providers
- [ ] Local llama.cpp provider behind DecisionProvider
- [ ] Contract and integration tests
- [ ] Target-hardware benchmark

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

## External implementation points
Capabilities that may require specialist implementation remain explicit architecture placeholders and must not be silently removed:
- `EXTERNAL_IMPLEMENTATION_REQUIRED: game-client-specific integration`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: anti-cheat compatibility/research`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: packet/network integration`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: client-specific bypass/injection work`

These are interfaces/roadmap points only. The minimal bring-up does not implement bypass, anti-cheat evasion, packet manipulation, or client injection. If a separately developed component is supplied later, it can be reviewed and integrated behind the appropriate adapter boundary.

## Legacy repository policy
`volobolo99/NosAi` remains a reference source. Nothing is considered migrated until its implementation is verified against the clean architecture and tests.
