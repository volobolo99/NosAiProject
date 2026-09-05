# ADR-0023 — Dashboard Cognitive Observability & Memory Explorer

**Status:** Accepted  
**Date:** 2026-09-05

## Context

NosAi now has a multi-timescale cognitive architecture, typed memory/outcome concepts, Guard/Safety boundaries and a practical operator dashboard. The existing dashboard is useful for runtime observation but does not yet expose the internal operational state in a coherent, inspectable way.

For development, university demonstration and debugging, the operator needs to understand:

- what NosAi currently believes about the world;
- what information is being considered;
- which candidate strategies were ranked;
- which plan was committed;
- what Guard/Safety allowed or rejected;
- what happened after execution;
- what the AI has learned and retained.

The interface must not expose or fabricate private model chain-of-thought. It must expose an auditable technical decision trace.

## Decision

Adopt two first-class Dashboard surfaces:

1. **Cognitive Flow** — live technical trace of the canonical cognition/execution pipeline.
2. **Memory Explorer** — read-only, structured exploration of working, episodic, semantic, procedural, outcome and runtime data.

The surfaces are backed by versioned typed read models and a bounded event stream. The WPF dashboard is a presentation layer and never becomes an execution authority.

### Cognitive Flow

The canonical visual flow is:

`Sensors → Temporal Fusion → Belief State → World Model → Attention → Prediction → Goals → Utility/Risk → HTN/GOAP → Candidate Plan → Guard → Trust → Safety → Execute → Verify → Re-observe`

The UI animates node completion and advances to the next event. Each event carries sequence, timestamp, correlation, provenance, confidence, classification and freshness.

Four cognitive timescales are selectable:

- Reflex
- Tactical
- Strategic
- Reflective

### Memory Explorer

Memory is exposed as a logical tree rather than as raw folders. Every record exposes identity, type, provenance, lifecycle, confidence, freshness, ruleset and evidence. JSON is available read-only for technical inspection.

The logical tree is not a promise that every category has already been implemented. Empty or unavailable categories remain explicitly marked `UNKNOWN`/`NOT INTEGRATED`.

## Security and safety constraints

- Dashboard queries are read-only by default.
- No dashboard memory operation can authorize a gameplay action.
- Guard and Safety remain authoritative for execution.
- LLM/ML/heuristic components cannot directly execute actions.
- Forbidden/privileged knowledge cannot be promoted to executable strategy through the UI.
- Simulated, cached and derived data must be labelled.
- No server DB, GM/admin tooling, hidden/debug state or privileged channel is introduced.
- The dashboard must never claim that an operation is real merely because a UI node was animated.

## Performance constraints

- Runtime event publication is bounded and non-blocking for critical execution paths.
- UI rendering is decoupled from runtime event frequency.
- Non-critical visual events may be coalesced/dropped under backpressure.
- Safety/audit events must not be silently dropped.
- Memory lists must use virtualization for large datasets.
- Historical replay is read-only and cannot execute a plan.

## Consequences

### Positive

- Development becomes substantially easier because cognition becomes inspectable.
- University demonstration can show real progression from observation to decision to verification.
- Memory, strategy learning and outcome data become understandable without opening implementation files.
- Debugging can correlate sensors, belief, planning and outcomes through one trace ID.

### Negative

- New read-model contracts and event infrastructure are required.
- The UI must handle high-frequency events without becoming a performance bottleneck.
- Every newly integrated memory subsystem needs an adapter and provenance mapping.

## Implementation order

D1 contracts → D2 bounded event stream → D3 memory query facade → D4 Cognitive Flow UI → D5 Memory Explorer UI → D6 historical replay → D7 performance tests → D8 real private-server E2E verification.

Reference: `docs/DASHBOARD_COGNITIVE_MEMORY_UX_SPEC.md`.
