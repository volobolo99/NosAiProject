# NosAiProject — Deterministic Agent Command Registry

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** ACTIVE

This registry complements the per-agent files under `docs/agents/phases/`. It gives every agent a bounded starting surface so agents do not wander through the repository or edit unrelated files.

## Mandatory execution rule

The per-agent phase document is the command of record. This registry defines the default domain paths. If a phase command names an exact file, that exact path overrides the directory rule.

Before editing, the agent must print internally (or in its handoff) the exact WRITE list it will use. If an expected file is absent, the agent must create it only if the phase command explicitly permits creation. Never rename/delete an existing file to make ownership easier.

## Canonical dependency set — all phases

READ ONLY unless explicitly owned:
- `docs/SOURCE_OF_TRUTH.md`
- `docs/ROADMAP_ESECUTIVA.md`
- `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md`
- `docs/NOSAI_ARCHITECTURE_BASELINE.md`
- `docs/UNPRIVILEGED_DEMO_SPEC.md`
- `docs/adr/`
- `docs/agents/AGENT_WORK_PROTOCOL.md`
- `docs/agents/FILE_OWNERSHIP_MATRIX.md`
- `docs/agents/AGENT_COMMAND_REGISTRY.md`
- `CLAUDE.md`
- `.cursor/rules/`

## Domain map

### AP-00 — Hardware / Runtime
- A1 Claude: `src/NosAi.Core/` runtime/hardware contracts and their direct unit tests.
- A2 Cursor: `src/NosAi.Runtime/` integration/bootstrap/runtime adapters and direct runtime tests.
- A3 Claude: Core duplicate/compatibility audit; only files explicitly assigned after inspection may be written.
- A4 Cursor: `src/NosAi.ControlPanel/` startup, runtime status and cognitive-dashboard integration.
- A5 Claude: `docs/`, test projects and benchmark documentation; no production ownership unless explicitly assigned.
- A6: solution/project files, cross-project wiring, conflict resolution and final integration.

### AP-01 — World Model
- A1 Claude: `src/NosAi.Core/WorldModel/` contracts, immutable state and domain value objects.
- A2 Cursor: perception-to-world-model adapters and sensor normalization in the existing perception/runtime locations.
- A3 Claude: temporal belief, prediction and derived-state algorithms in dedicated WorldModel/Planning files.
- A4 Cursor: runtime wiring from existing observation snapshots into the World Model; no contract redesign.
- A5 Claude: direct unit tests, deterministic fixtures, benchmarks and AP-01 documentation.
- A6: cross-project references and integration only.

### AP-02 — Multimodal Perception
- A1 Claude: perception contracts and observation DTOs.
- A2 Cursor: screen/OCR/CV/client-observable adapters.
- A3 Claude: multimodal fusion, confidence and contradiction handling.
- A4 Cursor: runtime ingestion, throttling and lifecycle wiring.
- A5 Claude: tests, fixtures, benchmark baselines and docs.
- A6: integration.

### AP-03 — Map Reconstruction
- A1 Claude: map/grid/topology contracts.
- A2 Cursor: map observation extraction and normalization.
- A3 Claude: reconstruction, connectivity and uncertainty algorithms.
- A4 Cursor: runtime map cache/update integration.
- A5 Claude: deterministic map tests, benchmarks and docs.
- A6: integration.

### AP-04 — Exploration / Navigation
- A1 Claude: navigation contracts and movement evidence types.
- A2 Cursor: observation/evidence adapters.
- A3 Claude: path planning, replanning and cost/risk algorithms.
- A4 Cursor: runtime navigation loop and movement verification.
- A5 Claude: navigation tests, benchmarks and practical-test documentation.
- A6: integration and Gate1/Gate3 acceptance.

### AP-05 — Combat
- A1 Claude: combat/action contracts.
- A2 Cursor: combat observation adapters and target/state evidence.
- A3 Claude: tactical planning, cooldown/resource reasoning and candidate ranking.
- A4 Cursor: runtime action orchestration through existing Guard/Trust/Safety boundaries.
- A5 Claude: deterministic combat tests and benchmark scenarios.
- A6: integration.

### AP-06 — Quest Intelligence
- A1 Claude: quest/objective contracts.
- A2 Cursor: quest observation and UI/event adapters.
- A3 Claude: objective decomposition, prerequisite reasoning and replanning.
- A4 Cursor: runtime quest state integration.
- A5 Claude: quest tests, fixtures and docs.
- A6: integration.

### AP-07 — Character / Inventory / Equipment
- A1 Claude: character, inventory and equipment contracts.
- A2 Cursor: client-observable inventory/character observation adapters.
- A3 Claude: loadout/equipment planning and resource constraints.
- A4 Cursor: runtime character-control integration and verification.
- A5 Claude: tests and docs.
- A6: integration.

### AP-08 — Strategic Autonomy
- A1 Claude: strategic contracts and goals.
- A2 Cursor: attention/state aggregation adapters.
- A3 Claude: strategic planner, utility/risk and long-horizon selection.
- A4 Cursor: orchestrator/runtime lifecycle integration.
- A5 Claude: strategy tests, simulations and docs.
- A6: integration.

### AP-09 — Memory / Learning / Simulation
- A1 Claude: memory/learning contracts and lifecycle states.
- A2 Cursor: persistence/runtime adapters.
- A3 Claude: outcome learning, simulation and conservative adaptation.
- A4 Cursor: runtime memory bridge and observability integration.
- A5 Claude: deterministic learning tests, benchmarks and docs.
- A6: integration.

### AP-10 — Autonomous Certification
- A1 Claude: certification contracts and evidence schema.
- A2 Cursor: certification observation adapters.
- A3 Claude: certification runner and deterministic acceptance logic.
- A4 Cursor: runtime/test-center integration.
- A5 Claude: release documentation, certification matrix and evidence reports.
- A6: final release gate.

## Shared-file serialization

The following are integration-owned by A6 unless a phase command explicitly says otherwise:
- `NosAi.sln`
- `Directory.Build.props`
- `*.csproj`
- `.github/workflows/*`
- cross-project registration/bootstrap files
- shared generated artifacts
- canonical ADRs

## Prohibited ownership expansion

An agent must not take ownership of:
- another agent's source/test file;
- `third_party/` source deletion or rewriting;
- server/admin/GM interfaces;
- hidden server state;
- privileged APIs;
- anti-cheat/detection bypasses;
- secret credentials;
- Dashboard execution authority.

## Phase transition

A phase is eligible to advance only when A6 records:

```text
all handoffs received
AND no unexpected deletions
AND ownership conflicts = 0
AND affected build = PASS
AND affected tests = PASS
AND documentation updated
AND safety boundary preserved
```

`Verified` additionally requires the real evidence demanded by the phase. Source code alone is never sufficient.
