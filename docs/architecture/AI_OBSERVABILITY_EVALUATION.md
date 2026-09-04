# AI Observability and Evaluation

## Purpose

Provide repeatable telemetry and offline evaluation for NosAi without granting telemetry or evaluation code any gameplay execution authority.

## Signals

- pipeline stage latency: p50/p95/p99
- planner success/failure and bounded-search exhaustion
- perception confidence and source provenance
- recovery transitions and safe-stop events
- Safety Gate allow/deny counts
- action verification success/failure
- allocation and memory-pressure measurements in benchmark builds

OpenTelemetry is the preferred external observability standard for traces, metrics and logs; the .NET SDK exposes these through the platform logging, metrics and Activity APIs.

## Evaluation dataset

Every evaluation case must be replayable from client-observable evidence. A case contains observations, expected uncertainty, candidate plans, safety decisions and verification outcomes. No server-admin state may be used as an oracle.

## Drift

Track distributions of confidence, observation freshness, entity counts, planner failures and verification failures. Drift is advisory and can request degradation/safe-stop; it must never bypass the deterministic authorization path.

## RL boundary

World-model RL and self-play remain offline/simulation capabilities. A trained policy can produce a candidate artifact, but promotion requires deterministic replay evaluation and the same Guard/Safety/authorization path as every other candidate.

## Acceptance gates

1. deterministic replay produces identical planner/ranking decisions;
2. provenance violations are zero;
3. missing/stale observations cause rejection or safe recovery;
4. p50/p95/p99 budgets are measured from real runtime traces;
5. safety rejection and recovery events are persisted;
6. no telemetry component can directly invoke gameplay execution.
