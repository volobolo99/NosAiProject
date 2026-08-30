# NosAi Local Orchestrator

This directory defines the local runner boundary for the NosAi autonomous AI workforce.

## Design

The runner is intentionally provider-neutral. It reads the repository control-plane files and invokes approved local adapters for Claude Code, Cursor, Git, and validation commands.

The runner MUST NOT contain secrets or API keys.

## Planned commands

```text
nosai status
nosai next
nosai plan
nosai run
nosai verify
nosai resume
nosai stop
```

## Execution model

1. Load `.nosai/ORCHESTRATOR_EXECUTION.md`.
2. Load `.nosai/PROJECT_STATE.md` and the approved task queue.
3. Refuse execution if the repository is in an unsafe state.
4. Select only an authorized GREEN task.
5. Delegate implementation to the configured local coding agent.
6. Run repository verification commands.
7. Delegate review to the configured reviewer agent.
8. Stop on YELLOW/RED gates or after three repair cycles.
9. Record evidence and update project state.
10. Create a normal Git commit only after all completion criteria pass.

## Current status

Bootstrap only. No automatic process is started by this repository, and no production code is changed by the orchestrator layer.

## Local prerequisites

The operator must install and authenticate the desired local tools separately. The runner should discover their executables rather than storing credentials in this repository.
