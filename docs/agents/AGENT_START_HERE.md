# NosAiProject — Agent Start Here

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE

This is the first operational page for Claude Code, Cursor Agent and the integration agent. It does not replace the phase command; it tells the agent how to execute that command safely.

## 1. Startup sequence

Before writing anything:

1. Read `CLAUDE.md` or `.cursorrules` for the active agent.
2. Read `docs/SOURCE_OF_TRUTH.md`.
3. Read `docs/ROADMAP_ESECUTIVA.md`.
4. Read `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md`.
5. Read `docs/NOSAI_ARCHITECTURE_BASELINE.md`.
6. Read `docs/agents/AGENT_WORK_PROTOCOL.md`.
7. Read `docs/agents/FILE_OWNERSHIP_MATRIX.md`.
8. Read `docs/agents/AGENT_COMMAND_REGISTRY.md`.
9. Read the exact assigned phase/agent command under `docs/agents/phases/`.
10. Read only the files explicitly named by that command and their directly required tests.

Do not perform repository-wide exploration unless the assigned command explicitly says `repository audit`.

## 2. Establish the execution boundary

Write down before implementation:

- `TASK_ID`
- `PHASE`
- `AGENT`
- `WRITE_FILES`
- `READ_FILES`
- `READ_ONLY_FILES`
- `DEPENDENCIES`
- `EXPECTED_TESTS`
- `EXPECTED_BUILD`

The phase command is authoritative. The registry is a routing aid. If they disagree, stop and ask the integration agent to resolve the conflict.

## 3. Git safety gate

Before changing a file:

1. Confirm the current branch and HEAD.
2. Confirm the target file exists or that the command explicitly permits creating it.
3. Inspect the current contents.
4. Confirm the file belongs to this agent.
5. Never replace a repository tree with a hand-built tree.
6. Never delete a file to resolve an ownership conflict.
7. Never edit another agent's file because it appears convenient.
8. Never touch `third_party/` deletion/provenance/license material unless an explicit provenance task owns the exact file.

If the starting HEAD changes while the task is running, stop, reload the affected files and re-evaluate the plan before committing.

## 4. Implementation gate

Implement complete, compilable files. Do not leave:

- TODO/FIXME
- pseudocode
- `...`
- empty/stub methods
- fake success paths
- commented-out replacement implementations
- hidden fallbacks that convert UNKNOWN into false/zero/empty
- mocks on production critical paths

Preserve the architecture invariant:

`Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking/Utility → Strategic Orchestrator → HTN/GOAP → Guard → Trust/Authorization → Safety → Execute → Verify → Re-observe`

Cognition proposes. Runtime authorizes and executes.

## 5. Validation gate

Run the smallest useful checks first, then the broader checks required by the command:

1. formatter/static analysis when configured;
2. targeted unit tests;
3. affected project build;
4. affected integration tests;
5. benchmark when the command requires a performance claim;
6. final diff inspection.

Never report a test or build as passed unless it was actually executed and its result is known.

## 6. Evidence gate

A handoff must distinguish:

- `Present`: artifact exists.
- `Implemented`: artifact is complete and locally validated to the available extent.
- `Integrated`: combined with required dependencies and validated.
- `Done`: phase acceptance conditions are met.
- `Verified`: real evidence required by the phase exists.

Source code alone cannot justify `Verified` for real-client behavior.

## 7. Stop conditions

Stop and report instead of improvising when:

- a required dependency is owned by another agent;
- a shared project/solution/workflow file must change;
- the command is ambiguous;
- current HEAD changed unexpectedly;
- a required public API would need an unassigned change;
- a safety boundary would be weakened;
- a test exposes an unrelated regression;
- a real-environment prerequisite is unavailable;
- a credential, privileged API or hidden state would be required.

The integration agent decides cross-owner changes.

## 8. Handoff format

Use the schema in `docs/agents/PHASE_HANDOFF_SCHEMA.md`. Include exact commands and results, not general statements such as `tests passed`.

## 9. Golden rule

**Do less, but make every changed file complete, attributable, testable and reversible.**
