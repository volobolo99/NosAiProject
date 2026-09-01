# NosAiProject — Architecture Baseline & Source of Truth

**Version:** 1.0
**Date:** 2026-08-30
**Status:** SUPERSEDED for new work as of 2026-09-01 (see `docs/adr/ADR-0015-adopt-roadmap-esecutiva-as-canonical-architecture.md`). Still the accurate description of the existing `NosAi.Runtime`/`NosAi.Protocol` implementation, which keeps building and keeps its tests; new work follows `docs/ROADMAP_ESECUTIVA.md` instead.

## 1. Purpose

This document is the architectural baseline for NosAiProject v1.0. It defines the canonical boundaries used for implementation, review, testing and AI-assisted development.

Existing code is not automatically authoritative when it conflicts with this baseline. A conflict must be documented and resolved through an Architecture Decision Record (ADR).

## 2. Canonical system flow

```text
PC Runtime
   │
   ├── Hardware / OS telemetry
   │
   ├── Real Client Integration
   │          │
   │          ▼
   │     Perception / Input
   │          │
   │          ▼
   │      World Model
   │          │
   │          ▼
   │     Decision / Policy
   │          │
   │          ▼
   │       Safety Gate
   │          │
   │          ▼
   │       Execution
   │          │
   │          ▼
   │      Verification
   │          │
   │          └──────────► Re-observe
   │
   └── Control Gateway ──► Control Panel
              │
              └──────────► Guard AI Smartphone
```

## 3. Architectural layers

### Runtime
Owns process lifecycle, orchestration, health, state transitions and controlled execution of the system.

### Live Integration
Owns adapters to supported real client/environment signals. It must not invent gameplay state when no real source exists.

### Perception
Converts external observations into validated internal observations. Providers must be explicit about whether data is live, simulated, cached or unavailable.

### World Model
Maintains the canonical state consumed by decision and monitoring components. It must distinguish unknown from zero/default values.

### Decision / Policy
Determines what the system intends to do from available state and policy. It must not bypass safety boundaries.

### Safety
Enforces trust boundaries, authorization, validation, capability restrictions and fail-safe behavior before execution.

### Execution
Performs only authorized and validated actions against supported targets.

### Verification
Confirms the expected result and feeds new observations back into the system.

### Network / Gateway
Provides authenticated transport, framing, versioning, session management and event delivery between runtime and authorized clients.

### Control Panel
Provides operational visibility and supported controls; it is not a replacement for runtime safety enforcement.

### Guard AI Smartphone
Provides remote monitoring and authorized controls through the same authenticated trust boundary as other clients.

### Hardware / Observability
Provides machine capability, resource telemetry, diagnostics, logging and health information.

### Storage
Provides persistence for explicitly durable state, sessions, configuration, audit records and other approved data.

## 4. Data classification

Every externally visible value must be classifiable as one of:

- `LIVE` — obtained from a real supported source;
- `DERIVED` — deterministically calculated from trusted input;
- `CACHED` — previously observed and explicitly marked stale/age-aware;
- `SIMULATED` — generated for development/testing only;
- `UNKNOWN` — no trustworthy value is currently available.

Production dashboards and decision paths must not silently present `SIMULATED` data as `LIVE`.

## 5. Trust boundaries

The minimum trust boundaries are:

1. local runtime process boundary;
2. real client/environment boundary;
3. network transport boundary;
4. authenticated smartphone/control-client boundary;
5. execution/safety boundary;
6. persistent-storage boundary.

Security validation must occur at the boundary where trust is established, not only in UI code.

## 6. Interface rules

- Prefer interfaces/contracts over direct cross-layer dependencies.
- Runtime orchestration must not embed transport implementation details.
- UI must not directly perform privileged execution.
- Safety checks must remain server/runtime-side and cannot depend on client UI behavior.
- Network messages must be versioned and validated.
- Unknown fields should be handled according to explicit compatibility policy.
- Public contract changes require an ADR and corresponding tests.

## 7. Real-vs-demo rule

Any demo, fixture or simulated provider must be clearly named and isolated. It must never be selected implicitly in a production path.

For Gate 1, the authoritative path is:

`bootstrap → real client connection → minimum real data → authenticated smartphone → coherent dashboard → error/disconnect handling`

The current repository contains substantial implementation along this path, but `RealClientConnector` is still a partial real-data source and several dashboard/telemetry paths contain mixed or demonstrative values. These areas remain verification work rather than being declared complete.

## 8. Verification hierarchy

```text
Present
  ↓
Integrated
  ↓
Done (build + local tests)
  ↓
Verified (integration / real evidence)
```

No release milestone may be considered complete from source inspection alone.

## 9. Architectural invariants

1. Safety cannot be bypassed by UI or network clients.
2. Real and simulated data are never silently interchangeable.
3. Unknown state is represented explicitly.
4. Execution requires an authorized and validated path.
5. Observations are the source of truth for verified state.
6. Every critical remote session has explicit lifecycle and failure semantics.
7. Security-sensitive events are auditable.
8. Components must fail closed where safety requires it.
9. Protocol changes are versioned.
10. Tests must validate behavior at the appropriate layer.

## 10. ADR index

| ADR | Decision | Status |
|---|---|---|
| ADR-0001 | Canonical layered architecture | Accepted |
| ADR-0002 | Real/demo data separation | Accepted |
| ADR-0003 | Safety boundary is runtime authoritative | Accepted |
| ADR-0004 | Verification required before release completion | Accepted |
| ADR-0005 | Protocol/API changes require explicit versioning | Accepted |

## 11. Change management

When a proposed implementation conflicts with this baseline:

1. stop the affected architectural change;
2. describe the conflict;
3. create/update an ADR;
4. evaluate compatibility and migration cost;
5. update tests/contracts;
6. only then implement the approved decision.
