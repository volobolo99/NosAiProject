# NosAiProject — AI Workforce Autonomy Policy

**Status:** ACTIVE
**Scope:** Cursor, Claude Code and future NosAi agents

## Objective
Enable autonomous implementation of approved roadmap work while preserving human control over scope, security, architecture and irreversible operations.

## Source of truth
Agents must use: `NOSAI_MASTER_ROADMAP.md`, `CLAUDE.md`, `docs/NOSAI_ARCHITECTURE_BASELINE.md`, `docs/adr/`, `.cursor/rules/`, then task-specific specifications.

If authoritative sources conflict, stop and report the conflict. Do not guess.

## Agent roles

### Claude Code — Lead Implementation Agent
Repository-wide implementation, tests, refactoring, build/verification and technical documentation.

### Cursor — Interactive Engineering Agent
Focused implementation, code navigation, debugging, local validation and targeted refactoring.

### Reviewer — Quality Gate
Inspect changes, verify acceptance criteria, identify regressions/security issues/architectural drift, and reject changes that fail the Definition of Done.

### Human / Project Owner
Scope and product decisions, unresolved architecture decisions, secrets and external credentials, destructive operations, final release acceptance and real-environment validation.

## Autonomous actions
Agents MAY read repository files, modify source/test/docs files, run approved local build/test/diagnostic commands, fix failures caused by the current task, create small coherent commits on isolated task branches, and update task state/evidence.

Agents MUST NOT expose or commit secrets; weaken security/safety boundaries; force-push or rewrite unrelated history; delete data outside task scope; introduce undocumented breaking API/protocol changes; claim `VERIFIED` without evidence; or invent missing requirements.

## Autonomy levels

**GREEN — automatic:** routine implementation, tests, documentation, local builds and non-breaking refactors.

**YELLOW — decision required if unspecified:** new dependencies, public contract changes, significant architecture changes, database/schema changes, protocol changes.

**RED — human approval required:** credentials/secrets, production operations, destructive actions, security-boundary changes, irreversible migrations, force-pushes, release publication, unresolved product decisions.

## Execution loop
`SELECT TASK → READ CONTRACTS → INSPECT → PLAN → IMPLEMENT → TEST → BUILD → REVIEW DIFF → DOCUMENT → COMMIT → UPDATE STATE → SELECT NEXT TASK`

Continue to the next task only after the current task passes its required quality gate.

## Failure policy
Build/test failures may receive bounded fixes within the current scope. After 2 focused correction cycles, stop and report. Architectural contradictions, missing required real environments, or unexpected concurrent changes require a stop/report state.

## Completion evidence
Every completed task records: milestone/task ID, files changed, commands executed, build result, test result, integration/real-environment result when applicable, verification level, and remaining risks/blockers.

## Branch discipline
One coherent task or tightly coupled task group per branch. Prefer pull requests for completed milestones. Never mix unrelated work.
