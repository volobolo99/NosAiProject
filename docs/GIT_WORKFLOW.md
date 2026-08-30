# NosAiProject — Git Workflow

**Version:** 1.0  
**Date:** 2026-08-30  
**Status:** ACTIVE

## Purpose

Define the Git operating model for humans and AI coding agents, prioritizing traceability, reviewability, rollback and protection against uncontrolled changes.

## Branches

- `main` — stable integration/release branch.
- `feature/<milestone>-<short-name>` — feature work.
- `fix/<short-name>` — corrective work.
- `docs/<short-name>` — documentation-only work.
- `refactor/<short-name>` — isolated refactoring.
- `test/<short-name>` — test-only work.

Non-trivial AI implementation should use a dedicated branch. Direct `main` commits are reserved for small, low-risk maintenance/documentation changes.

## Commits

Format: `<type>: <short description>`

Types: `feat`, `fix`, `test`, `refactor`, `docs`, `build`, `ci`, `security`.

One coherent purpose per commit. Do not mix unrelated refactors, generated artifacts or feature work.

## Pull requests

PRs should contain:

1. milestone/task ID;
2. problem and intended behavior;
3. architectural impact;
4. changed components/files;
5. build and test results;
6. security impact when relevant;
7. remaining risks/blockers;
8. affected ADR references.

## Pre-merge checklist

- Inspect the complete diff.
- Confirm no secrets, tokens, credentials or private keys.
- Build affected projects.
- Run relevant unit/integration/contract tests.
- Confirm documentation/contracts are synchronized.
- Confirm no unrelated files changed.
- Confirm the claimed verification level is supported by evidence.

## AI-agent safety

Agents must not:

- force-push or rewrite shared history;
- destroy another agent's work;
- silently resolve architecture conflicts;
- disable or weaken tests;
- commit secrets;
- claim real-environment verification without evidence.

If unexpected working-tree changes are found, inspect and report them before overwriting or reverting anything.

## Merge and rollback

Prefer reviewed PRs for implementation. Merge only when required checks pass. Squash noisy agent-generated histories when the resulting commit remains coherent and traceable.

For a bad release, prefer a revert commit over rewriting shared history.

## Releases

Use semantic release tags such as `v1.0.0`. A release tag must point to a validated commit and have corresponding release notes.
