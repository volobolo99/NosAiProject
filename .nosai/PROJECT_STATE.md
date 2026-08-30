# NosAiProject — AI Project State

## Current state

- Control plane: defined
- Autonomous workflow: defined
- Orchestrator contract: defined
- Production-code automation: not enabled by this document alone
- Human approval gates: mandatory

## Active task

The orchestrator must select the first eligible task from `docs/AI_WORK_QUEUE.md` according to roadmap priority and dependencies.

## State transition format

Use these states:

`READY -> PLANNING -> IMPLEMENTING -> TESTING -> REVIEWING -> COMPLETED`

Failure states:

`BLOCKED`, `NEEDS_HUMAN`, `FAILED`

## Required evidence per completed task

- task identifier
- files changed
- build command/result
- test command/result
- review result
- commit SHA
- blockers, if any

## Human decision log

No new decisions recorded by the orchestrator yet.
