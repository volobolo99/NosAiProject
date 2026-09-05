# NosAiProject — Agent Work Protocol v1.0

## Purpose
This document is the operating contract for Claude Code and Cursor multi-agent work. It exists to maximize parallelism without merge collisions, incomplete files or cross-agent regressions.

## Canonical startup
Read this file, `docs/ROADMAP_ESECUTIVA.md`, `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md`, `docs/NOSAI_ARCHITECTURE_BASELINE.md`, the assigned phase command, and only the explicitly listed dependencies.

## Six-agent topology
Default: 5 parallel implementation agents + 1 integration/release agent.

A1 Contracts/Domain; A2 Perception/Data; A3 Planning/Algorithms; A4 Runtime/Integration; A5 Tests/Benchmarks/Docs; A6 Integration Gate.

A1–A5 have disjoint write ownership. A6 starts only after A1–A5 complete. If only four agents are available, run A1–A4 and then use the strongest available agent as A6 after they finish.

## Non-negotiable synchronization
1. One source file has exactly one owner per phase.
2. Read-only dependencies may be inspected by every agent.
3. No agent edits another agent's owned file.
4. Cross-file API changes are declared in the command before implementation.
5. A6 alone resolves integration conflicts.
6. A phase cannot start until the previous phase is `Integrated` or better.

## Completion gate
Every owned file must be complete. No TODO/FIXME/pseudocode/ellipsis/stub/placeholder/intentional compile break. Every changed C# project must build. Every behavior change gets tests. Production critical paths never use test mocks.

## Handoff format
Each agent leaves: files changed, public contracts changed, tests run, build result, known assumptions, unresolved blockers, and exact integration notes.

## Verification levels
`Present` = file/code exists. `Integrated` = combined tree builds and tests pass for the phase. `Done` = acceptance criteria met in the available environment. `Verified` = real target evidence exists where required. Never upgrade a level without evidence.

## Safety
No agent may widen the product access boundary, bypass Guard/Trust/Safety, expose hidden server state, use privileged/admin data, or add detection-evasion behavior. UNKNOWN remains UNKNOWN.
