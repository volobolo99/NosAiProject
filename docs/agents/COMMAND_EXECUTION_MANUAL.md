# NosAiProject — Multi-Agent Command Execution Manual

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE  

## 1. Purpose

This manual is the operational entry point for running Claude Code and Cursor in parallel on NosAiProject. It is deliberately conservative: speed comes from disjoint ownership, not from letting multiple agents edit the same files.

## 2. Golden rule

**Never create a new Git tree from scratch.** Every repository change must be based on the current `main` tree or a branch created from the current `main` commit. Never delete or replace existing project files merely to simplify an integration.

Before any write:
1. fetch current `main`;
2. record current commit SHA;
3. inspect the target file and its current SHA when updating it;
4. verify the target is owned by the current agent;
5. make the smallest complete change;
6. test/build;
7. report the exact commit SHA.

## 3. Agent topology

| Agent | Tool | Primary responsibility | Parallel |
|---|---|---|---|
| A1 | Claude | contracts/domain models | yes |
| A2 | Cursor | perception/data/adapters | yes |
| A3 | Claude | algorithms/planning | yes |
| A4 | Cursor | runtime/integration/dashboard | yes |
| A5 | Claude | tests/benchmarks/docs | yes |
| A6 | Claude or Cursor | integration/release gate | after A1-A5 |

A1-A5 may run concurrently only when their write sets are disjoint. A6 is serialized.

## 4. Startup command

Copy the following command into the selected agent:

> You are agent `<A1-A6>` for phase `<AP-XX>`. Read `docs/agents/AGENT_WORK_PROTOCOL.md`, `docs/ROADMAP_ESECUTIVA.md`, `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md`, `docs/NOSAI_ARCHITECTURE_BASELINE.md`, `docs/agents/FILE_OWNERSHIP_MATRIX.md`, and your phase command file. Do not search the repository arbitrarily: use only the explicitly listed dependency paths first. Inspect current code before changing it. You own only the files listed by your command. Do not modify another agent's files. Produce complete compilable files only. No TODO, FIXME, pseudocode, ellipsis, stub, placeholder, disabled test, fabricated evidence, or intentional compile break. Run the required tests/build. If a dependency outside your ownership must change, stop and report it to A6 instead of editing it. Finish with the mandatory handoff.

## 5. Per-phase sequence

```text
A1 ─┐
A2 ─┤
A3 ─┤── parallel implementation ──> handoffs
A4 ─┤
A5 ─┘
             ↓
             A6
             ↓
      integration build/tests
             ↓
        phase acceptance
             ↓
          next phase
```

## 6. File ownership

A command file must contain four explicit lists:

### READ
Exact canonical specifications and dependency files the agent may inspect.

### WRITE
Exact files the agent may create or replace during that task.

### READ-ONLY
Files that may be inspected but never modified by that agent.

### FORBIDDEN
Files, directories or interfaces that the agent must not touch. This always includes another active agent's write set, `third_party/` source deletion, server/admin interfaces, hidden game state and safety bypasses.

If a needed file is not in WRITE, the agent does not edit it.

## 7. Complete-file rule

A successful task is not a partially implemented task. The agent must either deliver a complete implementation or report a blocker. Never commit a known incomplete file just to unblock another agent.

## 8. Cross-agent API changes

If A2 needs a contract change owned by A1:

```text
A2 discovers requirement
→ A2 records exact required API
→ A2 stops at boundary
→ A1 changes contract
→ A1 publishes handoff
→ A2 rebases/reloads current dependency
→ A2 completes implementation
```

A4/A6 owns project-file or cross-project integration changes unless the phase command explicitly says otherwise.

## 9. Integration gate A6

A6 must verify:
- all five handoffs exist;
- every changed file has exactly one owner;
- no accidental deletion;
- no unrelated changes;
- public API compatibility;
- project references and namespaces;
- tests and build;
- safety boundary;
- documentation consistency;
- third-party provenance;
- runtime evidence where the phase requires it.

A6 must compare the phase result with the phase starting commit. If any existing file disappeared unexpectedly, integration stops immediately.

## 10. Verification vocabulary

`Present` — file exists.  
`Implemented` — implementation is complete in source.  
`Integrated` — combined tree builds and phase tests pass.  
`Done` — acceptance criteria are satisfied.  
`Verified` — real target evidence exists where required.

Agents must never infer `Verified` from source presence.

## 11. Handoff template

```text
TASK: <AP-XX/A#>
AGENT: <Claude/Cursor>
START_COMMIT: <sha>
FINAL_COMMIT: <sha>
FILES_CREATED: <paths>
FILES_MODIFIED: <paths>
FILES_DELETED: none (or exact justified list)
CONTRACTS_CHANGED: <none or exact APIs>
TESTS: <exact commands + result>
BUILD: <exact command + result>
VERIFICATION_LEVEL: Present/Implemented/Integrated/Done/Verified
BLOCKERS: <none or exact blocker>
INTEGRATION_NOTES: <exact dependency/order information>
```

## 12. Four-agent fallback

When only four agents are available, use A1-A4 in parallel. A5 is deferred, not silently skipped. After A1-A4 finish, the strongest available agent performs A6. A5 must run before the phase can be marked `Done`.

## 13. Six-agent acceleration strategy

The agents should maximize parallelism by separating:
- contracts from consumers;
- observation from planning;
- runtime wiring from algorithms;
- implementation from validation.

Never accelerate by parallel editing of shared files.

## 14. Phase order

```text
AP-00 Hardware/Runtime
AP-01 World Model
AP-02 Multimodal Perception
AP-03 Map Reconstruction
AP-04 Exploration/Navigation
AP-05 Combat
AP-06 Quest Intelligence
AP-07 Character/Inventory/Equipment
AP-08 Strategic Autonomy
AP-09 Memory/Learning/Simulation
AP-10 Autonomous Certification
```

A later phase cannot bypass a blocked prerequisite phase.

## 15. Product boundary

NosAi remains an autonomous player for the permitted private educational/test environment. The gameplay truth/control path may use ordinary-client-observable data, legitimate local client information, screen/pixels/OCR/CV, local telemetry, standard Windows APIs and normal mouse/keyboard control. It must not use server databases, GM/admin tooling, hidden server state, privileged APIs, secret credentials, anti-cheat bypasses or external automation hardware.

Cognitive observability is read-only and technical. It is not private chain-of-thought and it never receives execution authority.
