# NosAiProject — Master Roadmap

**Version:** 1.1
**Date:** 2026-08-30
**Target:** NosAiProject v1.0 — reale, integrato, testato e verificato
**Repository:** `volobolo99/NosAiProject`
**Status:** SUPERSEDED for new work as of 2026-09-01 — see `docs/adr/ADR-0015-adopt-roadmap-esecutiva-as-canonical-architecture.md` and `docs/ROADMAP_ESECUTIVA.md`. This document still tracks the existing `NosAi.Runtime` milestones (kept running, kept tested); it is not extended with new milestones going forward.

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
- [x] `M003` — Establish source-of-truth technical specifications and ADRs. **DONE** (`docs/SOURCE_OF_TRUTH.md`, `docs/adr/ADR-0001`–`0005`)
- [x] `M004` — Add/validate `CLAUDE.md` for Claude Code. **DONE**
- [x] `M005` — Add/validate Cursor project rules. **DONE** (`.cursor/rules/`)
- [x] `M006` — Define Git branching, commit and review workflow. **DONE** (`docs/GIT_WORKFLOW.md`)
- [x] `M007` — Define build, test and release commands as reproducible procedures. **DONE** (`docs/BUILD_TEST_RELEASE.md`)

**Gate G0:** repository is understandable, buildable and safe for agent-assisted development.

## Phase 1 — Core Foundation
- [ ] `M010` — Consolidate solution/project structure.
- [x] `M011` — Central configuration and environment validation. **DONE** (`RuntimeEnvironmentValidator` in `src/NosAi.Runtime/Configuration/RuntimeEnvironmentValidator.cs`, enforced fail-closed in `Gate1BootstrapHost`; `tests/NosAi.Runtime.Tests/RuntimeEnvironmentTests.cs`)
- [x] `M012` — Structured logging and correlation identifiers. **DONE** (`CorrelationScope` in `src/NosAi.Runtime/Observability/RuntimeLogger.cs`, wired into `Gate1BootstrapHost`; `tests/NosAi.Runtime.Tests/RuntimeLoggerTests.cs`)
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
| 0 — Baseline & Governance | 7 | 7 | 0 | `DONE` |
| 1 — Core Foundation | 6 | 2 | 0 | `IN_PROGRESS` |
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

1. Close Gate 1 on the real Windows + NosTale + Guard AI path and record evidence (`M030`–`M047`, `M050`–`M058`, `M090`–`M098`, `M100`–`M109` as required by the circuit).
2. Promote Gate 1 items to `VERIFIED` only after real-environment evidence.
3. Then execute Core Foundation leftovers that are not already implied by Gate 1 (`M010`–`M015`) and Security (`M020`–`M027`).

# Change log

### 2026-08-30 — v1.1
- Marked M001 repository audit `DONE`.
- Marked M002 architecture baseline `DONE` using repository inspection and `docs/GATE1_COMPONENT_MAP.md`.
- Marked M003–M007 governance artifacts `DONE`.
- Added classified Gate 1 snapshot `gate1.snapshot.v1` and local Gate 1 bootstrap/dashboard path. Real-environment Gate 1 remains not `VERIFIED`.
- Added architectural baseline and explicit distinction between implementation and verification.
- Updated progress tracking and execution priority.

### 2026-09-01
- Real Windows + NosTale + Guard AI hardware was not available in this session (no matching client process, no `adb` device), so Gate 1 real-environment closure (`Next execution priority` item 1) stayed blocked; proceeded to item 3, Core Foundation.
- Marked M012 (structured logging and correlation identifiers) `DONE`: `ConsoleRuntimeLogger` always printed `correlationId=none` because nothing in the runtime ever started an `Activity`, and `Gate1BootstrapHost`'s own `_correlationId` was only ever passed as an ordinary log property on one line, invisible on every other line from the same run. Added `CorrelationScope` (`AsyncLocal`-backed) and wired it into `Gate1BootstrapHost`'s constructor/`DisposeAsync`. 14 new tests, no mocks; full suite 517/517 C# tests and the existing Python suite green.
- Marked M011 (central configuration and environment validation) `DONE`. Added `RuntimeEnvironmentValidator`: the preconditions the runtime depends on -- Windows (DPAPI custody, ADR-0010), a writable `data/` directory, and a configured trusted Guard key that actually parses -- are now established before the boot instead of surfacing as an `IOException` thrown from inside `RuntimeIdentity.LoadOrCreate`. Required checks fail closed, and `Unknown` on a required check blocks exactly as a failure does. 13 new tests, no mocks; full suite 530/530 C# and 248/248 Python.
- Deleted `src/NosAi.Runtime/Configuration/RuntimeOptions.cs`. Nothing referenced it and no document named it, but it declared `LiveInputEnabled` and `PacketInjectionEnabled` with a `SectionName` of `NosAi` -- a configuration class that looked like the gate on live input and packet injection while gating nothing. The real gate is `RuntimeSafetyPolicy`. Its `OperationTimeoutMs` duplicated `Gate1HostOptions`. Its one invariant (injection requires live input) was not carried over: ADR-0014 treats input and protocol paths as separate options, so the coupling has no basis to restore.
- Conflict reported, not overridden: a pending rewrite of `.cursorrules` would have instructed agents in anti-cheat evasion ("COMPLIANCE BYPASS", "ANOMALY DETECTION BYPASS", "Anti-Fingerprinting") and banned the project's real domain terms so the work would not be recognisable. ADR-0014 excludes detection evasion by name while permitting the data paths, so the rules were restated without those sections and with the safety invariants the rewrite had dropped.
- Wire version 4. Gate 1's Guard AI path was tested against the real device for the first time this session and the phone aborted its own process one second into every session: `pal_cipher.c:258 (AndroidCryptoNative_CipherUpdate): Parameter 'in' must be a valid pointer`. Heartbeats carried no payload, a heartbeat is not a handshake message so ADR-0009 seals it, and sealing nothing hands ChaCha20-Poly1305 a zero-length span -- which in C# always pins to a null pointer. The desktop BCL accepts that and the Android crypto PAL refuses it, so every local test on Windows passed while no real device ever survived its first heartbeat. Pre-existing since `4c8ead2`. `Heartbeat` and `HeartbeatAck` now carry `WireMessageTypes.HeartbeatPayload`, and `CurrentVersion` moves 3 -> 4 (ADR-0005) so a version-3 peer is refused at the header instead of crashing a phone. Golden cross-language vectors regenerated: only the version byte and the Poly1305 tag move, since the header is the AEAD associated data.
- Fixed `nosai/phone/adb.py`: `deploy` treated presence of the package as up to date, so it verified the freshness of the APK *file* and never of what was actually installed. The phone was running the 30 Aug build against a wire-v3 runtime and was refused with `invalid_header:unsupported_version` -- the exact failure `deploy.py` documents itself as existing to prevent. It now compares the installed `base.apk` against the local APK by hash rather than by timestamp, because the device clock and the PC clock need not agree; a hash that cannot be read, or a split install with no single file to compare, reinstalls rather than assuming.
- **Real-environment evidence, Gate 1 Guard AI channel (`VERIFIED`).** Real Windows 11 + real `NostaleClientX` (PID 19300, window `0x33089E`) + real Android device (NX809J, `9125322104AC`) over `adb reverse`. Observed on the runtime's own operator API: `connected=True authenticated=True sessionId=50c2c7e7df89`, held for 40+ seconds with heartbeats every ~2 s and no termination reason; before the fix the same sequence reached `authenticated=True` and then `peer_disconnected` after one second. The phone reported `CONNESSO`, `attached_os_session`, `runtime Healthy`, and the runtime's own safety state `disabled_by_operator [LIVE]`. Classification held throughout: hardware `LIVE`, and every unobserved value `UNKNOWN` with an explicit reason (`process_not_found`, `no_active_session`, `gameplay_provider_not_available`) rather than zero.
- **Wi-Fi transport (ADR-0007) verified on real hardware.** Every `adb reverse` tunnel was removed first (`adb reverse --list` empty) so USB could not be what was working. The phone found the PC by LAN discovery alone -- no address typed -- and the session came up after about four seconds: `connected=True authenticated=True sessionId=7f352b5b93e5`, held stable. The socket proves the path: `TCP 192.168.0.4:17471 <- 192.168.0.2:45702 ESTABLISHED`, phone Wi-Fi address to PC Wi-Fi address, with no tunnel in between.
- Still not verified, and not claimed: gameplay observation remains `UNKNOWN` (`gameplay_provider_not_available`) -- ADR-0012/ADR-0014 leave the provider unbuilt, so the channel carries a snapshot with no gameplay in it.
- **ADR-0010 key custody restored on the device.** The phone had been reporting `keystore_private_key_unavailable` and keeping its device key as a file inside the app -- the ADR's custody silently downgraded, on hardware, with the app itself saying so on screen and nothing failing. The cause was a C# type test: an AndroidKeyStore RSA key is `android.security.keystore2.AndroidKeyStoreRSAPrivateKey`, a class with no managed binding, so .NET Android wraps it in a generic proxy and `is IPrivateKey` answers false about an object that is a private key. `JavaCast<IPrivateKey>()` builds the interface invoker over the same Java instance. The phone now reports `Chiave del dispositivo: Android Keystore`, and the handshake was verified end-to-end with the Keystore signing: session `2f70ce79b475`, held stable. This code is inside `#if ANDROID`, so no Windows test can reach it; the device is the only thing that can confirm it.
- Fixed in the same pass: enrolment read the published key from the device log buffer, and `deploy` launched the app and collected immediately. `extract_public_key` already took the most recent block and its docstring already named this failure, but "most recent in the buffer" is not "from the run we just started" -- the app publishes a moment after launch, so the *previous* run's key was still newest, was returned rather than retried for, and was enrolled. The runtime then refused the handshake with `authentication_failed` and no indication why; it cost a full debugging cycle here. `deploy` now clears the log immediately before launching, so the only block enrolment can read belongs to this run. Verified against a deliberately poisoned buffer holding two stale blocks.
