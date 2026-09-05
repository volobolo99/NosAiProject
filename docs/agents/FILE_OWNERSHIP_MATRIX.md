# NosAiProject — File Ownership Matrix

**Version:** 1.0  
**Date:** 2026-09-05

## Ownership classes

- `A`: implementation owner
- `B`: test/documentation owner
- `I`: integration owner
- `R`: read-only dependency

## Rules

1. One implementation owner per file per phase.
2. Shared contracts are serialized.
3. Project files and solution files are integration-owned unless explicitly assigned.
4. Tests should be owned by the same domain agent or a dedicated validation agent.
5. Documentation can run in parallel when it does not modify canonical architecture decisions.
6. An agent must stop rather than edit a file owned by another active agent.

## Current domain allocation

| Domain | Primary | Secondary | Integration |
|---|---|---|---|
| Core contracts | Claude A1 | Claude A5 | A6 |
| Perception | Cursor A2 | Claude A5 | A6 |
| World model | Claude A1 | Cursor A4 | A6 |
| Navigation | Claude A3 | Cursor A2 | A6 |
| Combat | Cursor A4 | Claude A3 | A6 |
| Progression | Claude A3 | Cursor A4 | A6 |
| Memory/Knowledge | Claude A3 | Cursor A4 | A6 |
| Runtime/Gate3 | Cursor A4 | Claude A1 | A6 |
| Dashboard | Cursor A4 | Claude A5 | A6 |
| Tests/benchmarks | Claude A5 | Cursor A2 | A6 |
| Documentation | Claude A5 | Cursor A4 | A6 |

## Conflict protocol

If two tasks require the same file, the file is moved to A6 integration ownership. Agents provide complete patches or new files; A6 performs the final merge.
