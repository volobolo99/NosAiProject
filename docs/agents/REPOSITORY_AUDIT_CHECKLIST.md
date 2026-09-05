# NosAiProject — Repository Audit Checklist

**Version:** 1.0  
**Date:** 2026-09-05  
**Owner:** A6 Integration Gate  
**Status:** ACTIVE

This checklist is the mandatory technical audit before declaring AP-00 `Done` or `Verified`. It is an evidence checklist, not a permission to edit files outside the assigned ownership.

## 1. Repository integrity

- [ ] Record starting `main` HEAD.
- [ ] Confirm working branch/ref used by the agent.
- [ ] Confirm no unexpected deletion exists relative to the starting HEAD.
- [ ] Confirm no unrelated file modifications exist.
- [ ] Confirm `third_party/` is intact and provenance/license material is preserved.
- [ ] Confirm no generated/build artifacts were accidentally committed.
- [ ] Confirm no secrets, credentials or private configuration were introduced.

## 2. Canonical governance

- [ ] `CLAUDE.md` is present and consistent with the agent protocol.
- [ ] `.cursorrules` is present and consistent with the agent protocol.
- [ ] `docs/SOURCE_OF_TRUTH.md` identifies the canonical roadmap/spec/architecture documents.
- [ ] `docs/ROADMAP_ESECUTIVA.md` is the active execution roadmap.
- [ ] `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` is present.
- [ ] `docs/NOSAI_ARCHITECTURE_BASELINE.md` is present.
- [ ] `docs/agents/AGENT_WORK_PROTOCOL.md` is present.
- [ ] `docs/agents/FILE_OWNERSHIP_MATRIX.md` is present.
- [ ] `docs/agents/AGENT_COMMAND_REGISTRY.md` is present.
- [ ] `docs/agents/PHASE_HANDOFF_SCHEMA.md` is present.
- [ ] `docs/agents/AGENT_START_HERE.md` is present.

## 3. Solution and project graph

Inspect, do not modify unless A6 owns the change:

- `NosAi.sln`
- `Directory.Build.props`
- every `src/**/*.csproj`
- every `tests/**/*Tests.csproj`
- project references and package references
- target frameworks and Windows targeting settings
- startup objects and executable projects
- test project discovery used by CI/scripts

Acceptance:

- [ ] Every solution project path exists.
- [ ] Every project reference resolves to an existing project.
- [ ] No project silently references a forbidden layer.
- [ ] Core remains dependency-light according to the architecture baseline.
- [ ] Runtime/ControlPanel/Host dependency direction is explicit.
- [ ] Test projects reference the intended production projects.

## 4. Runtime foundation

Audit the AP-00 runtime surface and direct tests:

- hardware profiling
- runtime profile/budget selection
- process/window discovery
- client observation lifecycle
- runtime session lifecycle
- Gate1 status/telemetry
- Gate3 decision loop
- authorization/guard/safety boundaries
- error handling and cancellation
- logging/diagnostics

For each component record:

`State = Missing | Present | Implemented | Integrated | Blocked | Verified`

and attach concrete evidence.

## 5. Dashboard foundation

Audit only the interfaces required by AP-00:

- startup/bootstrap
- runtime status display
- Practical Test Center
- cognitive observability window
- memory explorer
- read-only boundary
- refresh/update lifecycle

Acceptance:

- [ ] Dashboard cannot authorize or execute game actions.
- [ ] Cognitive observability contains technical typed trace, not private chain-of-thought.
- [ ] UNKNOWN/BLOCKED states remain distinguishable from PASS.
- [ ] Memory explorer reads real persisted/runtime data only.
- [ ] If runtime and dashboard are separate processes, the data transport is explicitly identified; a same-process registry must not be described as cross-process telemetry.

## 6. Testability

- [ ] Targeted unit tests exist for each AP-00 contract family.
- [ ] Runtime test project is discoverable by the repository scripts/CI.
- [ ] Tests are deterministic where possible.
- [ ] Tests do not require live game/server state unless explicitly marked integration/environment tests.
- [ ] Failure output identifies the failing invariant.
- [ ] No test is described as passing unless it was actually executed.

## 7. Buildability

Record exact commands and results for the environment used. At minimum, when available:

```text
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --configuration Release
```

For broader validation:

```text
dotnet build NosAi.sln --configuration Release
dotnet test NosAi.sln --configuration Release
```

If the environment cannot execute a command, mark it `BLOCKED` and record why. Never convert `BLOCKED` to `PASS` by inference.

## 8. Architecture and safety

- [ ] Ordinary-client boundary is preserved.
- [ ] No server database/admin/GM/mod interface is introduced.
- [ ] No hidden/debug-only state is used as production truth.
- [ ] No privileged API is required for normal operation.
- [ ] Cognition has no execution authority.
- [ ] Guard/authorization/safety remain between planning and execution.
- [ ] Real-client behavior is never certified from simulation-only evidence.

## 9. AP-00 exit gate

AP-00 may advance only when all applicable items are evidenced and:

1. no unexpected deletions exist;
2. ownership conflicts are zero;
3. affected projects build successfully, or an explicit blocker is documented;
4. affected tests pass, or an explicit blocker is documented;
5. no unresolved contract contradiction remains;
6. documentation reflects the actual implementation;
7. third-party provenance remains intact;
8. the handoff uses `PHASE_HANDOFF_SCHEMA.md`;
9. `Verified` is used only where real evidence exists.

## 10. Audit report template

```text
TASK_ID:
PHASE: AP-00
AGENT:
START_HEAD:
END_HEAD:

INTEGRITY:
- unexpected deletions: 0/N
- unrelated modifications: 0/N
- third_party integrity: PASS/BLOCKED

PROJECT_GRAPH:
- solution paths: PASS/FAIL
- project references: PASS/FAIL
- package references: PASS/FAIL

RUNTIME:
- hardware: STATE + evidence
- lifecycle: STATE + evidence
- Gate1: STATE + evidence
- Gate3: STATE + evidence
- guard/safety: STATE + evidence

DASHBOARD:
- startup: STATE + evidence
- test center: STATE + evidence
- cognitive observability: STATE + evidence
- memory explorer: STATE + evidence

TESTS:
- exact command:
- result:

BUILD:
- exact command:
- result:

BLOCKERS:

FINAL_VERIFICATION_LEVEL:
NEXT_ACTION:
```

**Rule:** an empty evidence field is not a PASS.
