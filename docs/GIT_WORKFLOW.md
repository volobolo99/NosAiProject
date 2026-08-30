# NosAiProject — Git Workflow

**Version:** 1.0  
**Date:** 2026-08-30  
**Status:** ACTIVE

## 1. Purpose

This workflow defines how humans and AI coding agents modify NosAiProject. The objective is traceability, reviewability and protection against uncontrolled repository changes.

## 2. Branch model

- `main` — stable integration/release branch.
- `feature/<milestone>-<short-name>` — feature implementation.
- `fix/<short-name>` — corrective change.
- `docs/<short-name>` — documentation-only change.
- `refactor/<short-name>` — isolated refactoring.
- `test/<short-name>` — test-only change.

AI agents should work on a dedicated branch for non-trivial implementation. Direct commits to `main` are reserved for small, low-risk repository-maintenance changes when no review branch is required.

## 3. Commit rules

Use imperative, concise commit messages:

`<type>: <short description>`

Allowed common types:

- `feat` — functionality
- `fix` — bug fix
- `test` — tests
- `refactor` — behavior-preserving refactor
- `docs` — documentation
- `build` — build/tooling
- `ci` — CI automation
- `security` — security changes

Every commit should have one coherent purpose. Avoid giant mixed commits.

## 4. Pull requests

A PR should include:

1. milestone/task ID;
2. problem and intended behavior;
3. architectural impact;
4. files/components changed;
5. tests executed and results;
6. build result;
7. security impact where applicable;
8. remaining risks/blockers.

If an ADR is affected, reference it explicitly.

## 5. Required validation

Before merging:

- inspect the complete diff;
- ensure no secrets or credentials are present;
- build affected projects;
- run relevant tests;
- run integration/contract tests when boundaries change;
- verify documentation/contracts are synchronized;
- confirm no unrelated files were modified.

## 6. AI-agent rules

Agents must not:

- rewrite unrelated history;
- force-push or destroy another agent's work;
- silently resolve architectural conflicts;
- disable or weaken tests;
- commit secrets;
- claim real-environment verification without evidence.

If the working tree contains unexpected changes, inspect and report them before overwriting or reverting anything.

## 7. Merge policy

Prefer reviewed PRs for implementation work. Merge only when required CI/tests pass and the PR description accurately reflects the change.

Squashing is preferred for noisy agent-generated commit histories when the resulting commit remains logically coherent and traceable.

## 8. Rollback

For a bad release, prefer a revert commit over rewriting shared history. Recovery procedures must preserve auditability.

## 9. Release tags

Releases use version tags such as `v1.0.0`. A release tag must point to a validated commit and be accompanied by release notes.
