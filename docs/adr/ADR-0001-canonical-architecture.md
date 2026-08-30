# ADR-0001 — Canonical Layered Architecture

**Status:** Accepted  
**Date:** 2026-08-30

## Context

NosAiProject contains multiple runtime, integration, networking, UI, security and monitoring components. Agent-assisted development requires explicit boundaries so that local implementation choices do not silently change system architecture.

## Decision

NosAi adopts a layered architecture with explicit boundaries:

`Runtime → Integration/Perception → World Model → Decision/Policy → Safety → Execution → Verification`

Cross-cutting services include Network/Gateway, Security, Storage, Hardware and Observability. Control Panel and Guard AI Smartphone are clients of authenticated runtime services and are not authoritative execution layers.

## Consequences

- Responsibilities remain testable and replaceable.
- UI and remote clients cannot bypass runtime safety.
- Real client adapters can evolve without rewriting decision logic.
- Integration tests can validate each boundary independently.
- Architectural deviations require an ADR.
