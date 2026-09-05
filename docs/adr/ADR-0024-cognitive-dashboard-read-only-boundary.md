# ADR-0024 — Cognitive Dashboard read-only boundary

**Status:** Accepted
**Date:** 2026-09-05

## Decision

The Control Panel exposes a dedicated **Cervello AI & Memoria** surface. It is an observability product, not an execution authority.

The surface has two logical views:

1. **Cognitive Flow** — technical decision trace, node state, candidates, confidence, risk, outcome and timestamps.
2. **Memory Explorer** — logical categories over real persisted/runtime data with search and a read-only inspector.

The UI never exposes or reconstructs private chain-of-thought. It displays a typed technical decision trace: observations, evidence references, candidate summaries, selected action, guard/safety status, execution result and verification.

## Truth model

- Missing evidence is `UNKNOWN`.
- A UI animation must correspond to an observed trace event; visual state must never imply execution.
- Filesystem entries are reported only when actually present.
- Memory categories are logical projections and do not imply that a category exists.
- The Dashboard cannot bypass Guard, Trust or Safety.
- Read-only inspection must not mutate memory, runtime state, keybinds or execution policy.

## Performance

The dashboard consumes bounded trace data. Rendering is downstream of the runtime and must not block the reflex/safety loop. Historical memory browsing is lazy and searchable; large stores must be virtualized or paged before production-scale datasets are exposed.

## Security

Secrets, private keys, authentication material and privileged server-side data are excluded from the explorer. The dashboard boundary remains within the ordinary-client observation model defined by the project source of truth.

## Acceptance criteria

- Cognitive view opens from the main Control Panel and via `Ctrl+F10`.
- Memory explorer shows real files only and supports inspection without writes.
- Trace view shows typed events and latest decision without claiming hidden model reasoning.
- Dashboard has no direct action effector and cannot authorize execution.
- Tests cover bounded trace retention and decision projection.
