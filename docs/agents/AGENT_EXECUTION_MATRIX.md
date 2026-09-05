# NosAiProject — Agent Execution Matrix

## Agent roles

| Agent | Primary responsibility | Typical writes |
|---|---|---|
| A1 | Contracts/domain | Core contracts, DTOs, domain models |
| A2 | Perception/data | sensors, capture, normalization, observation adapters |
| A3 | Planning/algorithms | planners, scoring, simulation, navigation/combat algorithms |
| A4 | Runtime/integration | orchestration, Gate wiring, persistence adapters |
| A5 | Tests/benchmarks/docs | tests, fixtures, benchmarks, phase docs |
| A6 | Integration gate | conflict resolution, solution build, full tests, release evidence |

## Parallel rule
A1–A5 may run simultaneously only when their write sets are disjoint. They may read each other's contracts but must not modify them. A6 is sequential.

## Phase lifecycle
`READY → PARALLEL_WORK → HANDOFFS_COMPLETE → INTEGRATION → BUILD → TEST → SECURITY_REVIEW → DOCUMENTATION → INTEGRATED`.

Any failure returns the phase to `PARALLEL_WORK` with a new explicit task; never silently patch around a failure.

## Handoff artifact
Every agent command creates/updates a completion note in the same phase folder after implementation. A6 uses these notes as the integration checklist.

## Commit policy
One coherent commit per agent task where practical. A6 may create the integration commit. Never force-push or rewrite unrelated history.
