# NosAiProject — Agent Session Checkpoint

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE

Use this file as a protocol template for every Claude/Cursor work session. It is deliberately a template: agents must create the checkpoint in their own session notes/branch rather than overwrite this canonical file.

## SESSION START

```text
TASK_ID:
PHASE:
AGENT: Claude | Cursor
ROLE: A1 | A2 | A3 | A4 | A5 | A6
START_HEAD:
START_BRANCH:

WRITE_FILES:
READ_FILES:
READ_ONLY_FILES:
DEPENDENCIES:
EXPECTED_TESTS:
EXPECTED_BUILD:

CANONICAL_DOCS_LOADED:
- CLAUDE.md / .cursorrules: YES/NO
- SOURCE_OF_TRUTH: YES/NO
- ROADMAP_ESECUTIVA: YES/NO
- AUTONOMOUS_PLAYER_SPEC: YES/NO
- ARCHITECTURE_BASELINE: YES/NO
- AGENT_WORK_PROTOCOL: YES/NO
- FILE_OWNERSHIP_MATRIX: YES/NO
- AGENT_COMMAND_REGISTRY: YES/NO
- PHASE_COMMAND: YES/NO
```

## PRE-EDIT GATE

```text
TARGET_FILES_EXIST:
OWNERSHIP_CONFIRMED:
DIRECT_DEPENDENCIES_INSPECTED:
PUBLIC_API_CHANGE_REQUIRED: YES/NO
SHARED_FILE_REQUIRED: YES/NO
EXTERNAL_DEPENDENCY_REQUIRED: YES/NO
SAFETY_BOUNDARY_CHANGE: YES/NO
START_HEAD_STILL_CURRENT: YES/NO
```

If `OWNERSHIP_CONFIRMED=NO`, `SHARED_FILE_REQUIRED=YES`, `EXTERNAL_DEPENDENCY_REQUIRED=YES`, `SAFETY_BOUNDARY_CHANGE=YES`, or `START_HEAD_STILL_CURRENT=NO`, stop and escalate to A6.

## IMPLEMENTATION CHECKPOINT

```text
FILES_CREATED:
FILES_MODIFIED:
FILES_NOT_MODIFIED:

COMPLETE_FILES_ONLY: YES/NO
TODO_OR_FIXME: NONE / FOUND
STUBS_OR_PSEUDOCODE: NONE / FOUND
FAKE_SUCCESS_PATHS: NONE / FOUND
UNKNOWN_PRESERVED: YES/NO
EXECUTION_AUTHORITY_ADDED_TO_COGNITION: NO/YES
```

## VALIDATION CHECKPOINT

```text
FORMATTER/STATIC_ANALYSIS:
TARGETED_TEST_COMMAND:
TARGETED_TEST_RESULT:
AFFECTED_BUILD_COMMAND:
AFFECTED_BUILD_RESULT:
INTEGRATION_TEST_COMMAND:
INTEGRATION_TEST_RESULT:
BENCHMARK_COMMAND/RESULT:

DIFF_REVIEW:
- unexpected deletions: 0/N
- unrelated changes: 0/N
- secrets/config leakage: NONE/FOUND
- third_party impact: NONE/EXPLICIT
- ownership violations: 0/N
```

Do not fill a result with `PASS` unless the command was actually executed and its result observed.

## SESSION END / HANDOFF

```text
END_HEAD:
VERIFICATION_LEVEL: Present | Implemented | Integrated | Done | Verified

CHANGED_FILES:
NOT_CHANGED_FILES:
EXACT_TESTS_AND_RESULTS:
EXACT_BUILD_AND_RESULTS:
KNOWN_LIMITATIONS:
BLOCKERS:
DEPENDENCIES_FOR_NEXT_AGENT:
NEXT_RECOMMENDED_ACTION:

REAL_ENVIRONMENT_EVIDENCE: NONE | ATTACHED/REFERENCED
```

## Concurrency rule

The checkpoint is not a lock. Git remains the source of truth for synchronization. If `START_HEAD != current HEAD`, reload the affected files before continuing. Never blindly replay edits on top of a changed base.

## Recovery rule

If an unexpected deletion or broad tree change appears, stop immediately. Preserve the observed HEAD and diff information and hand the incident to A6. Do not reconstruct a tree from memory and do not delete files to make the diff appear clean.
