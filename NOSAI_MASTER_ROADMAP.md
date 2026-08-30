# NosAiProject — Master Roadmap

**Version:** 1.1
**Date:** 2026-08-30
**Target:** NosAiProject v1.0 — reale, integrato, testato e verificato
**Repository:** `volobolo99/NosAiProject`

## Current verified baseline

### M001 — Repository audit
**Status:** `DONE`

The repository audit identified an existing multi-component implementation spanning Runtime, Gate 1, Live Client Integration, Network/Gateway, Hardware, Storage, Navigation, Miniland, dashboard and tests.

### M002 — Architecture baseline
**Status:** `DONE`

The canonical Gate 1 architecture is mapped by `docs/GATE1_COMPONENT_MAP.md`.

Critical path:

`PC runtime bootstrap → real client connection → minimum real data acquisition → Guard AI smartphone → coherent dashboard → error/disconnect handling`

Important finding: substantial implementation exists, but several components remain `Partial` or `Integrated/Partial` because real data sources and real end-to-end evidence are not complete. In particular, `RealClientConnector` provides a structured baseline snapshot but is not yet a complete gameplay-data source.

Existing implementation must be classified separately as `Present`, `Integrated`, `DONE`, or `VERIFIED`. File existence never implies `VERIFIED`.

## Status model

| Status | Meaning |
|---|---|
| `TODO` | Not started |
| `IN_PROGRESS` | Active implementation |
| `BLOCKED` | Waiting for dependency/environment/decision |
| `DONE` | Implemented and locally validated |
| `VERIFIED` | Integrated and verified against acceptance criteria |
| `DEFERRED` | Explicitly postponed |

## Engineering gates

Every implementation stream follows:

`SPEC → PLAN → IMPLEMENT → BUILD → TEST → INTEGRATE → REVIEW → VERIFY`

AI-agent rules:

- Do not silently change public APIs or protocols.
- Do not remove or weaken tests to make a build pass.
- Do not introduce dependencies without documenting the reason.
- Do not change security boundaries without updating the specification.
- Prefer small, reviewable commits.
- Stop on architectural contradictions instead of inventing undocumented behavior.
- Preserve compatibility unless a breaking change is explicitly required.

# Master execution plan

## Phase 0 — Baseline & AI-agent governance
- [x] `M001` — Audit repository structure, projects, dependencies, TODOs and obsolete code. **DONE**
- [x] `M002` — Establish canonical architecture and component boundaries. **DONE**
- [ ] `M003` — Establish source-of-truth technical specifications and ADRs.
- [ ] `M004` — Add/validate `CLAUDE.md` for Claude Code.
- [ ] `M005` — Add/validate Cursor project rules.
- [ ] `M006` — Define Git branching, commit and review workflow.
- [ ] `M007` — Define build, test and release commands as reproducible procedures.

**Gate G0:** repository is understandable, buildable and safe for agent-assisted development.

## Phase 1 — Core Foundation
- [ ] `M010` — Consolidate solution/project structure.
- [ ] `M011` — Central configuration and environment validation.
- [ ] `M012` — Structured logging and correlation identifiers.
- [ ] `M013` — Dependency Injection and lifecycle boundaries.
- [ ] `M014` — Standard error/result model and exception policy.
- [ ] `M015` — Core interfaces and contracts required by downstream components.

**Gate G1:** core services compile and can be tested independently.

## Phase 2 — Security & Identity
- [ ] `M020` — Complete RSA/key-management verification.
- [ ] `M021` — Secure key storage and lifecycle.
- [ ] `M022` — Onboarding/enrollment flow.
- [ ] `M023` — Authentication.
- [ ] `M024` — Authorization and role/capability model.
- [ ] `M025` — Secure communication and certificate validation.
- [ ] `M026` — Replay/tampering/input validation controls.
- [ ] `M027` — Security test suite and audit.

**Gate G2:** security-sensitive operations cannot be bypassed through normal application paths.

## Phase 3 — Runtime
- [ ] `M030` — Complete Runtime Gate 1 implementation.
- [ ] `M031` — Runtime startup/initialization lifecycle.
- [ ] `M032` — Runtime shutdown lifecycle.
- [ ] `M033` — Health/state model.
- [ ] `M034` — Watchdog and failure detection.
- [ ] `M035` — Recovery and deterministic state transitions.

**Gate G3:** runtime state transitions are deterministic, observable and recoverable.

## Phase 4 — Real Client Integration
- [ ] `M040` — Complete `RealClientConnector`.
- [ ] `M041` — Target process discovery and validation.
- [ ] `M042` — Window/session detection.
- [ ] `M043` — Connection/session lifecycle.
- [ ] `M044` — Client state detection.
- [ ] `M045` — Disconnect/reconnect handling.
- [ ] `M046` — Failure and timeout handling.
- [ ] `M047` — Real-environment integration tests.

**Gate G4:** client integration is verified against the actual supported environment.

## Phase 5 — Network Layer
- [ ] `M050` — Network abstraction and transport boundaries.
- [ ] `M051` — Protocol implementation.
- [ ] `M052` — Message framing.
- [ ] `M053` — Serialization/deserialization.
- [ ] `M054` — Message validation and versioning.
- [ ] `M055` — Timeout/retry policy.
- [ ] `M056` — Connection management.
- [ ] `M057` — Event transport.
- [ ] `M058` — Network diagnostics.

**Gate G5:** authenticated end-to-end communication works reliably under expected failure conditions.

## Phase 6 — Control Panel Gateway
- [ ] `M060` — Gateway architecture.
- [ ] `M061` — API endpoint contracts.
- [ ] `M062` — Authentication middleware.
- [ ] `M063` — Authorization enforcement.
- [ ] `M064` — Command handling and validation.
- [ ] `M065` — Event streaming.
- [ ] `M066` — Session/connection management.
- [ ] `M067` — Rate limiting and abuse controls.
- [ ] `M068` — Audit logging.

**Gate G6:** Control Panel ↔ Gateway ↔ Runtime works end-to-end.

## Phase 7 — Event System
- [ ] `M070` — Canonical event model.
- [ ] `M071` — Event bus.
- [ ] `M072` — Event routing.
- [ ] `M073` — Subscriptions.
- [ ] `M074` — Filtering.
- [ ] `M075` — Required persistence.
- [ ] `M076` — Replay/recovery semantics.
- [ ] `M077` — Telemetry integration.

**Gate G7:** component-to-component events are defined, observable and reliable.

## Phase 8 — Hardware Profiler
- [ ] `M080` — Hardware discovery.
- [ ] `M081` — CPU/RAM/GPU metrics.
- [ ] `M082` — Storage metrics.
- [ ] `M083` — Network adapter information.
- [ ] `M084` — Capability detection.
- [ ] `M085` — Initial profiling.
- [ ] `M086` — Continuous monitoring.
- [ ] `M087` — Resource/performance validation.

**Gate G8:** hardware profile is reliable and consumable by supported components.

## Phase 9 — Control Panel UI
- [ ] `M090` — Dashboard shell.
- [ ] `M091` — System status.
- [ ] `M092` — Runtime status.
- [ ] `M093` — Network status.
- [ ] `M094` — Security status.
- [ ] `M095` — Events and event stream.
- [ ] `M096` — Logs and diagnostics.
- [ ] `M097` — Configuration.
- [ ] `M098` — Error/recovery UX.

**Gate G9:** operator can understand system state and perform supported operations without direct code access.

## Phase 10 — Mobile App
- [ ] `M100` — Mobile architecture and API boundary.
- [ ] `M101` — Authentication.
- [ ] `M102` — Device pairing/onboarding.
- [ ] `M103` — Mobile dashboard.
- [ ] `M104` — Runtime monitoring.
- [ ] `M105` — Notifications.
- [ ] `M106` — Event view.
- [ ] `M107` — Authorized remote commands.
- [ ] `M108` — Security controls.
- [ ] `M109` — Offline/reconnection handling.
- [ ] `M110` — Android release build.
- [ ] `M111` — iOS release build, if included in scope.

**Gate G10:** supported smartphone client connects to the real backend and survives reconnect/failure scenarios.

## Phase 11 — Automated Testing
- [ ] `M120` — Unit-test coverage for critical logic.
- [ ] `M121` — Integration tests.
- [ ] `M122` — Network tests.
- [ ] `M123` — Security tests.
- [ ] `M124` — Runtime tests.
- [ ] `M125` — Client integration tests.
- [ ] `M126` — Control Panel E2E tests.
- [ ] `M127` — Mobile E2E tests where applicable.
- [ ] `M128` — Failure-injection tests.
- [ ] `M129` — Reconnection/recovery tests.
- [ ] `M130` — Load/stress tests.
- [ ] `M131` — Regression suite.
- [ ] `M132` — Automated CI test pipeline.

**Gate G11:** critical paths have repeatable automated verification.

## Phase 12 — Observability & Operations
- [ ] `M140` — Metrics.
- [ ] `M141` — Structured logs.
- [ ] `M142` — Distributed tracing where applicable.
- [ ] `M143` — Health/readiness endpoints.
- [ ] `M144` — Diagnostics bundle.
- [ ] `M145` — Crash/error reporting.
- [ ] `M146` — Performance/resource monitoring.
- [ ] `M147` — Operational alerts.

**Gate G12:** failures can be diagnosed from telemetry.

## Phase 13 — Deployment & Release Engineering
- [ ] `M150` — Reproducible build pipeline.
- [ ] `M151` — Semantic/versioned release strategy.
- [ ] `M152` — Release artifacts.
- [ ] `M153` — Configuration and environment management.
- [ ] `M154` — Installation procedure.
- [ ] `M155` — Upgrade procedure.
- [ ] `M156` — Rollback procedure.
- [ ] `M157` — Backup/recovery procedure.
- [ ] `M158` — Production configuration validation.

**Gate G13:** clean environment can be provisioned and upgraded repeatably.

## Phase 14 — Final Security & Reliability Audit
- [ ] `M160` — Threat model review.
- [ ] `M161` — Attack-surface review.
- [ ] `M162` — Authentication review.
- [ ] `M163` — Authorization review.
- [ ] `M164` — Secrets/key-management review.
- [ ] `M165` — Dependency vulnerability review.
- [ ] `M166` — Input-validation review.
- [ ] `M167` — Network-security review.
- [ ] `M168` — Logging/privacy review.
- [ ] `M169` — Failure-mode analysis.

**Gate G14:** release candidate passes security and reliability acceptance criteria.

## Phase 15 — Real-World Validation
- [ ] `M170` — Install on clean target environment.
- [ ] `M171` — Cold-start validation.
- [ ] `M172` — Real onboarding.
- [ ] `M173` — Real authentication/connection.
- [ ] `M174` — Real client integration.
- [ ] `M175` — Real network validation.
- [ ] `M176` — Real Control Panel validation.
- [ ] `M177` — Real smartphone validation.
- [ ] `M178` — Reboot test.
- [ ] `M179` — Disconnect/reconnect test.
- [ ] `M180` — Crash/recovery test.
- [ ] `M181` — Long-running stability test.
- [ ] `M182` — Performance validation.
- [ ] `M183` — Security validation.
- [ ] `M184` — End-to-end acceptance test.

**Gate G15:** all critical workflows operate correctly in the real supported environment.

## Phase 16 — v1.0 Release
- [ ] `M190` — Feature freeze.
- [ ] `M191` — Final bug-fix cycle.
- [ ] `M192` — Full regression.
- [ ] `M193` — Technical documentation.
- [ ] `M194` — Operator manual.
- [ ] `M195` — Installation guide.
- [ ] `M196` — Troubleshooting guide.
- [ ] `M197` — Release notes.
- [ ] `M198` — Version/tag creation.
- [ ] `M199` — NosAiProject v1.0 release.

**Gate G16:** release is reproducible, documented and accepted for the declared v1.0 scope.

# Definition of Done

A milestone is `DONE` only if implementation is complete, code compiles, relevant tests pass, no known regression is introduced, required documentation/contracts are updated, and the change is traceable in Git.

A milestone is `VERIFIED` only if integration/end-to-end acceptance passes and, where applicable, real-environment validation and security/operational requirements are satisfied with evidence recorded in the associated issue/PR/documentation.

# Dependency strategy

`Foundation → Security → Runtime → Client/Network → Gateway/Events → Hardware → UI → Mobile → Testing → Operations → Deployment → Audit → Real Validation → Release`

Parallel work is allowed only when dependency boundaries and contracts are stable.

# AI development workflow

For each task: read specification and existing implementation; identify dependencies; produce a plan; implement the smallest coherent change; add/update tests; build; run diagnostics; inspect the diff; update contracts/docs; report blockers instead of inventing undocumented architecture.

Recommended prompt:

> Implement milestone `<ID>` from `NOSAI_MASTER_ROADMAP.md`. First inspect the repository and relevant specification. Do not change public APIs or protocols unless explicitly required. Implement the smallest coherent change, add/update tests, build affected projects, run relevant tests, and report exact files changed, verification results, remaining risks and blockers.

# Progress tracking

| Phase | Milestones | Done | Verified | Status |
|---|---:|---:|---:|---|
| 0 — Baseline & Governance | 7 | 2 | 0 | `IN_PROGRESS` |
| 1 — Core Foundation | 6 | 0 | 0 | `TODO` |
| 2 — Security & Identity | 8 | 0 | 0 | `TODO` |
| 3 — Runtime | 6 | 0 | 0 | `TODO` |
| 4 — Client Integration | 8 | 0 | 0 | `TODO` |
| 5 — Network | 9 | 0 | 0 | `TODO` |
| 6 — Gateway | 9 | 0 | 0 | `TODO` |
| 7 — Events | 8 | 0 | 0 | `TODO` |
| 8 — Hardware | 8 | 0 | 0 | `TODO` |
| 9 — Control Panel UI | 9 | 0 | 0 | `TODO` |
| 10 — Mobile | 12 | 0 | 0 | `TODO` |
| 11 — Testing | 13 | 0 | 0 | `TODO` |
| 12 — Observability | 8 | 0 | 0 | `TODO` |
| 13 — Deployment | 9 | 0 | 0 | `TODO` |
| 14 — Security Audit | 10 | 0 | 0 | `TODO` |
| 15 — Real Validation | 15 | 0 | 0 | `TODO` |
| 16 — v1.0 Release | 10 | 0 | 0 | `TODO` |

**Overall completion:** `TBD` — implementation count is not a valid product-completion percentage.

# Next execution priority

1. `M003` — source-of-truth technical specifications and ADRs;
2. `M004` — `CLAUDE.md`;
3. `M005` — Cursor rules;
4. `M006` — Git workflow;
5. `M007` — reproducible build/test/release commands;
6. execute the Gate 1 critical path with real-environment evidence;
7. promote milestones to `VERIFIED` only after evidence.

# Change log

### 2026-08-30 — v1.1
- Marked M001 repository audit `DONE`.
- Marked M002 architecture baseline `DONE` using repository inspection and `docs/GATE1_COMPONENT_MAP.md`.
- Added architectural baseline and explicit distinction between implementation and verification.
- Updated progress tracking and execution priority.
