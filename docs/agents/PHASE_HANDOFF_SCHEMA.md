# NosAiProject — Phase Handoff Schema

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE

Every Claude/Cursor agent must finish with a machine-readable, human-auditable handoff. The handoff is evidence for A6 and the next phase.

## Required fields

```text
TASK_ID: <unique task identifier>
PHASE: <AP-00..AP-10>
AGENT: <A1..A6 + Claude/Cursor>
START_HEAD: <40-char commit SHA>
END_HEAD: <40-char commit SHA>

WRITE_FILES:
- <exact path>

CHANGED_FILES:
- <exact path>

NOT_MODIFIED:
- <important owned/read-only paths intentionally untouched>

IMPLEMENTATION:
- <concise description of completed behavior>

DEPENDENCIES:
- <exact dependency path or NONE>

TEST_COMMANDS:
- <exact command>

TEST_RESULTS:
- <PASS/FAIL/BLOCKED + relevant output summary>

BUILD_COMMANDS:
- <exact command>

BUILD_RESULTS:
- <PASS/FAIL/BLOCKED + relevant output summary>

BENCHMARKS:
- <exact command/result or NOT REQUIRED>

DIFF_REVIEW:
- unexpected files: <NONE or list>
- deletions: <NONE or exact list + reason>
- secrets detected: <NONE or BLOCKED>
- ownership conflicts: <NONE or list>

SAFETY_REVIEW:
- ordinary-client boundary preserved: <YES/NO>
- runtime remains authoritative for authorization/safety: <YES/NO>
- cognition has execution authority: <NO>
- third_party provenance preserved: <YES/NO/NOT TOUCHED>

VERIFICATION_LEVEL: <Present|Implemented|Integrated|Done|Verified>
BLOCKERS:
- <NONE or exact blocker>

HANDOFF_TO: <A6 or next named agent>
NEXT_ACTION: <one concrete next action>
```

## Rules

1. `START_HEAD` must be recorded before implementation.
2. `END_HEAD` must be the actual resulting commit.
3. Every changed path must appear explicitly.
4. A deletion requires an explicit command-level authorization and a documented reason. Otherwise the result is `BLOCKED`.
5. `Verified` requires the phase's real acceptance evidence; passing unit tests alone is insufficient when real-client behavior is required.
6. If a command was not executed, say `NOT RUN`; never infer a PASS.
7. If a blocker belongs to another agent, name the owner and exact file/API dependency.
8. A6 may reject a handoff that omits evidence, ownership, or diff review.

## Minimal example

```text
TASK_ID: AP-04-A3-NAV-001
PHASE: AP-04
AGENT: A3-Claude
START_HEAD: <sha>
END_HEAD: <sha>
WRITE_FILES:
- src/NosAi.Core/Navigation/PathPlanner.cs
CHANGED_FILES:
- src/NosAi.Core/Navigation/PathPlanner.cs
TEST_COMMANDS:
- dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj --filter FullyQualifiedName~Navigation
TEST_RESULTS:
- PASS
BUILD_COMMANDS:
- dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
BUILD_RESULTS:
- PASS
VERIFICATION_LEVEL: Implemented
BLOCKERS:
- NONE
HANDOFF_TO: A6
NEXT_ACTION: integrate after all AP-04 parallel handoffs are complete
```
