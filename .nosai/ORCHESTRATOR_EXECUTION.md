# NosAiProject — Orchestrator Execution Contract

## Purpose

Define the machine-executable operating contract for the autonomous AI workforce. This document is the bridge between the repository control plane and a future local runner on the developer PC.

## Source of truth

The runner MUST read, in this order:

1. `NOSAI_MASTER_ROADMAP.md`
2. `docs/AI_AUTONOMY_POLICY.md`
3. `docs/AI_AGENT_ROLES.md`
4. `docs/AI_AUTONOMOUS_WORKFLOW.md`
5. `.nosai/ORCHESTRATOR.md`
6. `.nosai/PROJECT_STATE.md`
7. `.nosai/tasks/` when present

Repository files override generated context. Never infer project requirements from chat history alone.

## Execution loop

```text
LOAD STATE
  -> SELECT NEXT AUTHORIZED TASK
  -> CHECK DEPENDENCIES
  -> INSPECT RELEVANT FILES
  -> PLAN
  -> IMPLEMENT
  -> FORMAT / STATIC ANALYSIS
  -> BUILD
  -> TEST
  -> REVIEW
  -> DOCUMENT
  -> UPDATE STATE
  -> COMMIT
  -> SELECT NEXT TASK
```

## Task authorization

A task may execute autonomously only when:

- it is present in the approved roadmap/work queue;
- all dependencies are satisfied;
- its risk is GREEN under `AI_AUTONOMY_POLICY.md`;
- required tools are available;
- no unresolved human decision is recorded;
- the working tree is in a known state.

YELLOW tasks require a human approval checkpoint before implementation. RED tasks always stop and request human intervention.

## Safety invariants

The runner MUST NOT:

- expose or print secrets;
- commit credentials, tokens, private keys, or local environment files;
- force-push;
- rewrite shared history;
- delete the repository or large groups of files without approval;
- bypass failing tests merely to obtain a green build;
- silently change roadmap requirements;
- claim a task is complete without evidence.

## Failure policy

For an implementation failure:

1. capture the failing command and error;
2. classify the failure;
3. ask the reviewer/architect agent for a diagnosis;
4. allow at most 3 repair cycles for one task;
5. if still failing, mark the task `BLOCKED` and stop.

No autonomous agent may endlessly retry the same failure.

## Completion evidence

A task can be marked `DONE` only when all applicable checks pass:

- build succeeds;
- tests succeed;
- no new critical analyzer/security findings;
- implementation matches acceptance criteria;
- documentation/state are updated;
- reviewer returns `PASS`;
- a Git commit identifies the completed change.

## Human escalation payload

When stopping for the user, produce:

```text
STATUS: BLOCKED | APPROVAL_REQUIRED
TASK: <task id>
REASON: <short reason>
EVIDENCE: <commands/results/files>
DECISION_REQUIRED: <exact question>
SAFE_NEXT_ACTION: <what can happen after approval>
```

## Local runner interface

The future local runner should expose these logical operations:

- `status` — show current project/task state
- `next` — select the next authorized task
- `plan` — generate an implementation plan
- `run` — execute one authorized task
- `resume` — continue after a human approval
- `verify` — build/test/review current work
- `stop` — safely stop autonomous execution

The repository contract intentionally does not prescribe a specific vendor CLI. The local adapter may invoke Claude Code, Cursor, shell commands, Git, or other approved tooling while preserving this contract.
