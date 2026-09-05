# NosAiProject — AI Agent Operating Model

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE

This document defines how Claude and Cursor are expected to cooperate on NosAiProject. It is intentionally operational: the goal is to maximize autonomous throughput without sacrificing repository integrity.

## Roles

| Agent | Primary role | Typical ownership |
|---|---|---|
| A1 Claude | contracts/domain | Core contracts, immutable domain types |
| A2 Cursor | perception/data | client-observable adapters, normalization |
| A3 Claude | planning/algorithms | planners, ranking, prediction, strategy |
| A4 Cursor | runtime/integration | runtime lifecycle, adapters, dashboard wiring |
| A5 Claude | validation | tests, benchmarks, evidence, documentation |
| A6 Claude/Cursor | integration gate | shared files, conflict resolution, final build/test |

The exact phase command overrides this default table.

## Parallel execution model

A1–A5 may work in parallel only when their WRITE lists are disjoint. A6 starts only after the required handoffs exist.

### Parallel work is allowed for

- independent contracts and algorithms;
- independent adapters;
- independent tests for already-stable APIs;
- independent documentation/evidence.

### Parallel work is forbidden for

- the same file;
- the same project/solution file;
- cross-project registration/bootstrap;
- shared public contracts while consumers are being changed;
- the same generated artifact;
- any unresolved API migration.

When a cross-agent API must change, the contract owner changes it first. Consumers reload the new contract and then implement against it. Do not maintain competing versions of the same contract.

## Autonomous loop

For each task, the agent should execute:

`LOAD → BOUNDARY CHECK → INSPECT → PLAN → IMPLEMENT → TEST → BUILD → DIFF REVIEW → HANDOFF`

### LOAD
Read the canonical documents and the exact command.

### BOUNDARY CHECK
Confirm WRITE/READ/READ-ONLY paths and safety constraints.

### INSPECT
Inspect the current file contents and direct dependencies only.

### PLAN
Choose the smallest complete change that satisfies the command. Avoid speculative architecture.

### IMPLEMENT
Write complete production-quality files. Preserve public compatibility unless the command explicitly authorizes an API change.

### TEST
Run focused deterministic tests first.

### BUILD
Build the affected project(s). If the environment prevents execution, report `BLOCKED` rather than assuming success.

### DIFF REVIEW
Check changed paths, deletions, ownership, secrets, generated files and safety boundaries.

### HANDOFF
Produce the exact schema in `docs/agents/PHASE_HANDOFF_SCHEMA.md`.

## Autonomous decision policy

An agent may decide autonomously when:

- the change is inside its explicit ownership;
- the intended behavior is defined by canonical docs or existing contracts;
- the change is reversible and testable;
- no security/safety boundary changes;
- no shared file is required.

An agent must stop for A6 when:

- ownership is ambiguous;
- a shared file must change;
- a public contract needs redesign;
- another agent's incomplete work is a prerequisite;
- a deletion appears necessary;
- an external dependency is required;
- real-client evidence is required but unavailable;
- the proposed solution would cross the ordinary-client boundary.

## No-fabrication policy

Agents must never manufacture:

- test results;
- runtime observations;
- packet observations;
- client memory facts;
- performance numbers;
- real-server evidence;
- Git history;
- credentials or configuration values.

A missing observation is `UNKNOWN` or `BLOCKED`, depending on whether the system can continue safely.

## Evidence ladder

`Code exists` < `Tests pass` < `Affected project builds` < `Integrated` < `Real environment evidence` < `Verified`

The agent must report the highest level actually supported by evidence.

## Change-size policy

Prefer one coherent purpose per commit. A large phase may contain multiple small commits, but each commit must be independently understandable and must not contain unrelated cleanup.

## Recovery policy

If an agent detects an unexpected deletion or broad tree change:

1. stop immediately;
2. record the current HEAD;
3. do not create a replacement tree;
4. inspect the diff against the recorded starting HEAD;
5. identify the exact offending operation;
6. ask A6 to recover or revert safely;
7. only resume after the repository state is confirmed.

## Practical priority

When choosing between more features and stronger foundations, prefer in this order:

1. repository integrity;
2. buildability;
3. deterministic tests;
4. runtime safety and authorization;
5. real observation quality;
6. integration;
7. performance;
8. feature breadth.

This ordering prevents Claude/Cursor from producing a large but untestable autonomous player.
