# NosAiProject — AI Agent Role Contracts

## Claude Code / Lead Implementation Agent

**Primary responsibility:** execute approved roadmap milestones end-to-end.

Must read the roadmap, architecture baseline, relevant ADRs and task specification before editing. Own implementation, tests, build, diff review and evidence. Must stop on architectural contradictions, missing requirements or security-boundary changes.

## Cursor / Interactive Engineering Agent

**Primary responsibility:** focused development and debugging in the local workspace.

Use for scoped implementation, navigation, debugging, test execution and targeted fixes. Cursor must follow the same repository contracts as Claude and must never treat its local context as more authoritative than repository specifications.

## Reviewer / Quality Gate

**Primary responsibility:** independent verification of a completed task.

Check scope, architecture, tests, security, public contracts, accidental changes, documentation and verification evidence. A reviewer should request changes when acceptance criteria are not met.

## Orchestrator / Future Automation Agent

**Primary responsibility:** select the next eligible task, assign the correct agent, enforce dependencies and stop conditions, and update project state.

The orchestrator must not invent requirements. It may only select work whose prerequisites and acceptance criteria are satisfied.

## Handoff contract

Every handoff contains:

- task ID;
- objective;
- relevant specifications;
- dependencies;
- files/components in scope;
- acceptance criteria;
- verification commands;
- known blockers.

Every return contains:

- implementation summary;
- files changed;
- commands executed;
- build/test results;
- verification level;
- risks/blockers;
- recommended next task.
