# NosAiProject — AI Orchestrator Contract

## Purpose

Define the control plane for autonomous development of NosAiProject. The orchestrator coordinates planning, implementation, validation and review while preserving human approval gates.

## Source of truth

Priority order:
1. `NOSAI_MASTER_ROADMAP.md`
2. `CLAUDE.md`
3. `docs/AI_AUTONOMY_POLICY.md`
4. `docs/AI_AGENT_ROLES.md`
5. `docs/AI_WORK_QUEUE.md`
6. architecture/ADR documentation
7. repository state and tests

## Agent responsibilities

### Architect / Planner
- Select the next eligible task.
- Inspect dependencies and current repository state.
- Produce an implementation plan.
- Identify approval gates before implementation.

### Engineering Agent
- Implement only the assigned task.
- Add or update tests.
- Build and run relevant tests.
- Never overwrite unrelated changes.

### Reviewer / QA
- Inspect the complete diff.
- Verify acceptance criteria.
- Verify tests and build evidence.
- Reject weakened tests, security regressions or scope creep.

## Autonomous loop

`LOAD STATE -> SELECT TASK -> INSPECT -> PLAN -> GATE CHECK -> IMPLEMENT -> TEST -> BUILD -> REVIEW -> FIX IF NEEDED -> DOCUMENT -> COMMIT -> UPDATE STATE -> NEXT TASK`

## Retry policy

- A failed build/test may be corrected autonomously when the failure is clearly caused by the current task.
- Maximum autonomous correction cycles per task: 3.
- After 3 failed cycles, stop and request human intervention.
- Never loop by repeatedly changing tests merely to obtain a pass.

## Mandatory stop conditions

Stop immediately when:
- a RED action is required;
- a YELLOW decision has no documented answer;
- requirements conflict;
- unrelated uncommitted changes are detected and cannot be safely isolated;
- a security-sensitive behavior is unclear;
- a destructive operation is proposed;
- required credentials/secrets are missing;
- build/test evidence cannot be obtained.

## Completion contract

A task is complete only when:
- implementation exists;
- acceptance criteria are satisfied;
- relevant tests pass;
- build passes;
- final diff has been reviewed;
- documentation/state is updated;
- a Git commit exists;
- no unresolved blocker remains.

The orchestrator must never claim success without evidence.
