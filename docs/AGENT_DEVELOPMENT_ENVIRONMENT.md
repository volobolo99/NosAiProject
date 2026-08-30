# NosAiProject — AI Agent Development Environment

**Status:** ACTIVE
**Milestone:** M008

## Objective

Provide one operational contract for Cursor and Claude Code so both agents follow the same architecture, validation and Git discipline.

## Authoritative documents

1. `NOSAI_MASTER_ROADMAP.md`
2. `CLAUDE.md`
3. `docs/NOSAI_ARCHITECTURE_BASELINE.md`
4. `docs/adr/`
5. `.cursor/rules/`
6. `docs/GIT_WORKFLOW.md`
7. `docs/BUILD_TEST_RELEASE.md`
8. `docs/AGENT_EXECUTION_CHECKLIST.md`

## Agent loop

`READ → INSPECT → PLAN → IMPLEMENT → TEST → BUILD → REVIEW DIFF → DOCUMENT → REPORT`

## Ownership model

- **Claude Code:** repository-wide implementation, tests, refactoring and verification work.
- **Cursor:** interactive editing, navigation, debugging and focused implementation.
- **Human/project owner:** scope decisions, acceptance decisions and unresolved architectural choices.

Agents may implement approved decisions but must not silently invent architecture.

## Parallel-agent safety

Agents working concurrently must use isolated branches/worktrees where supported. Before modifying a file, inspect its current state and avoid overwriting unexpected changes.

## Definition of evidence

Every completion report must identify:

- exact commands executed;
- build result;
- test result;
- integration result when applicable;
- real-environment result when applicable;
- files changed;
- remaining risks/blockers.

## Stop conditions

An agent must stop and report when:

- the requested change conflicts with an accepted ADR;
- required information is missing and cannot be inferred safely;
- a public contract would need an undocumented breaking change;
- a security boundary would be weakened;
- unexpected repository changes would be overwritten;
- verification requires an unavailable real environment.

## Gate 1 priority

After governance, implementation priority follows the critical path rather than UI-first development:

`Runtime bootstrap → real client connection → minimum real data → authenticated transport → Guard AI smartphone → coherent dashboard → error/disconnect recovery`
