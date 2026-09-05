# Cognitive Live Trace

## Status

Gate 3 publishes technical cognitive observability for the operator Dashboard.

The trace is intentionally not an LLM chain-of-thought. It exposes only typed runtime events: node, status, summary, evidence, confidence, cycle and timestamp.

## Runtime path

`Gate3DecisionLoop.RunOnceAsync` creates a cycle id, publishes observation and belief/world-model stages, executes the real Gate 3 orchestrator, then publishes planner, candidate, Guard, Safety, Execute, Verify and Re-observe states.

The operator surface reads the process-local `CognitiveObservabilityRegistry`. The dashboard has no execution authority.

## Evidence rules

- UNKNOWN remains UNKNOWN.
- No simulated value is presented as live.
- A decision is committed only when the runtime reports the corresponding outcome.
- Safety/Guard refusal is visible as rejection, never success.
- The trace is bounded in memory to prevent unbounded growth.

## Verification status

Implemented and wired at source level. Physical private-server E2E remains a separate verification gate and is not claimed by this document.
