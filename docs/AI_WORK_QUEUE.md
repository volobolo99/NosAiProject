# NosAiProject — AI Work Queue

This queue is the handoff point between the project roadmap and autonomous agents.

## Queue rules

1. Select only tasks whose dependencies are satisfied.
2. Work one coherent task at a time unless two tasks are explicitly independent.
3. Record the owner agent before implementation.
4. Do not skip acceptance criteria.
5. A task moves to `DONE` only after build and required local tests pass.
6. A task moves to `VERIFIED` only after the required integration/real-environment evidence exists.
7. Blocked tasks remain blocked until the dependency is resolved.

## Current priority

### TASK-M003 — Technical specifications and ADRs
**Status:** READY
**Owner:** Claude Code
**Reviewer:** Cursor / human project owner
**Source:** `NOSAI_MASTER_ROADMAP.md`

Objective: establish the source-of-truth technical specifications and ADR structure required for autonomous development.

Acceptance criteria:
- relevant architecture decisions are recorded in `docs/adr/`;
- no existing accepted behavior is contradicted;
- specifications identify contracts and boundaries clearly;
- documentation is internally consistent;
- affected links/references are valid.

Verification:
- inspect documentation diff;
- validate referenced paths;
- run repository documentation/build checks when available.

### TASK-M004 — Claude Code contract
**Status:** READY
**Owner:** Claude Code
**Source:** `NOSAI_MASTER_ROADMAP.md`

Objective: maintain and validate `CLAUDE.md` as the Claude Code operating contract.

### TASK-M005 — Cursor rules
**Status:** READY
**Owner:** Cursor
**Source:** `NOSAI_MASTER_ROADMAP.md`

Objective: maintain Cursor project rules consistent with `CLAUDE.md` and the autonomy policy.

### TASK-M006 — Git workflow
**Status:** READY
**Owner:** Claude Code
**Source:** `NOSAI_MASTER_ROADMAP.md`

Objective: formalize branch, commit, review and merge discipline for agent-assisted development.

### TASK-M007 — Build/test/release procedures
**Status:** READY
**Owner:** Claude Code
**Source:** `NOSAI_MASTER_ROADMAP.md`

Objective: document reproducible commands and expected verification evidence.

## Autonomous progression

After M003–M007, the orchestrator should select the next roadmap milestone on the Gate 1 critical path, subject to dependencies and approval gates.
