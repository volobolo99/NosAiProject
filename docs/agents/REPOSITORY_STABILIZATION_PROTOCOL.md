# NosAiProject — Repository Stabilization Protocol

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE

## Purpose

This protocol defines the mandatory stabilization pass before autonomous multi-agent development proceeds to the next implementation phase.

## Order

1. Inventory repository files and projects.
2. Identify duplicate contracts, compatibility layers and conflicting implementations.
3. Map project references and public contracts.
4. Validate canonical architecture and execution boundaries.
5. Validate tests and build configuration.
6. Validate Dashboard/runtime integration.
7. Validate cognitive observability integration.
8. Validate Navigation, Progression, Knowledge and Outcome domains.
9. Validate third-party provenance without deleting GPL/LGPL material.
10. Generate file-level ownership assignments for parallel agents.
11. Generate synchronized A1-A6 task documents for every active phase.
12. Integrate only after all assigned work has complete handoffs.
13. Build and test the complete solution.
14. Record evidence and update roadmap status.

## Definition of Complete

A stabilization task is complete only when the affected files are complete, compile, tests pass, no TODO/FIXME/stub remains in the affected implementation, documentation is updated, and the result has an explicit evidence record.

## Parallelism Rule

Agents may work concurrently only on disjoint file ownership. Shared contracts, project files, generated files and integration points are serialized through the Integration Agent. No agent may overwrite another agent's work.

## Truth Rule

`Present`, `Implemented`, `Integrated`, `Tested`, `Done` and `Verified` are distinct states. Never upgrade a state without evidence.

## Boundary Rule

The gameplay path remains limited to ordinary-client-observable data and permitted local client interfaces. No server database, GM/admin tooling, hidden server state, privileged APIs, anti-cheat evasion or secret credentials may be introduced.

## Required Handoff

Every agent must report: files changed, complete implementation summary, tests executed, build result, known limitations, integration dependencies and exact commit SHA.
