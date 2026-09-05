# NosAiProject — File Ownership Matrix

**Version:** 1.0  
**Date:** 2026-09-05

## Rules
1. One implementation owner per file per phase.
2. Shared contracts are serialized.
3. Project/solution files are integration-owned unless explicitly assigned.
4. Tests should be owned by the domain agent or validation agent.
5. An agent stops rather than editing a file owned by another active agent.
6. If two tasks require the same file, A6 owns the final integration.

## Domain allocation
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
