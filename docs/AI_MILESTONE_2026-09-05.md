# AI Milestone — 2026-09-05

## Completed through requested point 6

1. Deterministic bounded GOAP planner added under `src/NosAi.Core/Planning/Goap/`.
2. Provenance-aware memory contracts and bounded in-memory store added under `src/NosAi.Core/Memory/`.
3. Navigation abstraction and deterministic bounded A* grid planner added under `src/NosAi.Core/Navigation/`.
4. Deterministic Sequence/Selector routine primitives and source-aware perception contracts added.
5. Fail-closed recovery controller and observability/evaluation specification added.
6. Deterministic planning, provenance and recovery tests added.

## Architectural constraints preserved

- Core remains dependency-free: its project file has no ProjectReference or PackageReference. fileciteturn150file0L2-L2
- Existing live action authorization remains outside these AI components. The runtime StepGuardChain evaluates Shape, Geometry, Authority, Policy, Occupancy and Projection in order and short-circuits on refusal. fileciteturn148file0L2-L2
- Unknown provenance is not accepted as gameplay truth.
- AI, telemetry, recovery and planning do not gain direct execution authority.
- RL/world-model/self-play remain offline candidate-generation capabilities.
- Third-party GPL/LGPL material is untouched and remains in `third_party`.

## Validation status

The repository's CI already discovers `.NET` test projects and runs them when present. fileciteturn152file0L2-L2 The new tests therefore participate in the existing test discovery path.

**Important:** point 6 is reached, but Gate 4 is not declared certified. Certification still requires an actual Release build/test run, analyzer checks, allocation measurements, p99 measurements and physical validation against the real private test runtime.
