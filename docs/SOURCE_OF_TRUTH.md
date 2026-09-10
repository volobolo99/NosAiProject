# NosAiProject — Source of Truth

**Version:** 2.3  
**Date:** 2026-09-10  
**Status:** ACTIVE

This file defines which project documents are authoritative. When documents disagree, stop and resolve the conflict with an ADR.

## Canonical documents

1. `docs/ROADMAP_ESECUTIVA.md` — canonical development order and gates.
2. `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` — product capabilities, autonomy target and non-privileged boundary.
3. `docs/NOSAI_ARCHITECTURE_BASELINE.md` — layer boundaries, trust boundaries and architectural invariants.
4. `docs/UNPRIVILEGED_DEMO_SPEC.md` — reproducibility and permitted observation/control boundary.
5. `docs/adr/*.md` — accepted architectural decisions; later accepted ADRs override earlier decisions where explicitly stated.
6. `.claude/CLAUDE.md` — agent development rules: the mandatory 5-phase production protocol.
7. `third_party/README.md`, `third_party/manifests/*`, `third_party/provenance/*` — external-source provenance and reuse rules.
8. `docs/mcp/README.md`, `docs/mcp/CHIEF_RUNBOOK.md`, `docs/mcp/DEVELOPMENT_CONTRACTS.md`, `contracts/mcp-hub-001.json` — MCP Hub behavior, Chief operations, contracts and boundaries.
9. `contracts/ledger.json` and `docs/CONTRACT_MAP.md` — authoritative contract status, files, tests, blockers and open questions.
10. `docs/SYSTEM_MAP.md` — authoritative module boundaries and communication paths.
11. `docs/AGENT_COORDINATION.md` — authoritative multi-agent task packet, ownership, handoff and audit protocol.
12. `docs/mcp/ROLE_CATALOG.md` and `schemas/mcp_employee_role.schema.json` — stable employee identities, model bindings, capabilities and forbidden actions.
13. `docs/FUNCTION_INDEX.md`, `docs/FUNCTION_INDEX.json` and `docs/CONTRACT_SIGNATURE_TASKS.md` — generated function navigation, coverage limits and unresolved contract signatures.
14. `docs/REMAINING_WORK.md` — prioritized work still required, with acceptance evidence.
15. `docs/REPOSITORY_ORDER.md` and `docs/REPOSITORY_INVENTORY_2026-09-10.json` — directory ownership, archival/removal rules and current tree inventory.

## Root front-door documents

The Markdown files at the repository root — `PROJECT.md`, `REQUIREMENTS.md`, `ARCHITECTURE.md`,
`ROADMAP.md`, `DECISIONS.md`, `TASKS.md`, `TEST_PLAN.md`, `CHANGELOG.md` — are entry points, not
sources of truth. Each one points at the canonical document listed above and must never restate or
contradict it. When content changes, change the canonical document; the front door only changes when
the pointer itself is wrong.

Two root documents are canonical for their own subject because nothing else covers it:
`AGENTS.md` (development roles and the model assigned to each) and `COST_POLICY.md` (model routing
and cost policy). Verified model prices live in `scripts/model_prices.json`, which is the single
source for cost classes.

## AI programming navigation

Start at [AI_PROGRAMMING_GUIDE.md](AI_PROGRAMMING_GUIDE.md).
[AI_PROJECT_STEPS.md](AI_PROJECT_STEPS.md) maps canonical AP phases to MCP Lab integration.
[AI_DEVELOPMENT_MAP.json](AI_DEVELOPMENT_MAP.json) is a compact navigation index, not a second status ledger.
[AI_DELIVERY_PROTOCOL.md](AI_DELIVERY_PROTOCOL.md) defines implementation handoffs.

## Development-support documents

- `docs/agents/EXECUTION_QUEUE.md` — work queue: what the next step is.
- `docs/agents/phases/` — per-phase status records of work already delivered.
- `docs/BUILD_TEST_RELEASE.md` — reproducible build/test/release procedure.
- `docs/GIT_WORKFLOW.md` — Git workflow.
- `docs/TESTING.md` — testing strategy.
- `docs/RELEASE_CHECKLIST.md` — release checklist.
- `docs/CONTROLLO_PERSONAGGIO_ARCHITETTURA.md` — character-control domain boundary.
- `docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md` — actuation and verification details.
- `docs/PROGRESSION_ENGINE_SPEC.md` — progression/equipment domain specification.
- `docs/PROTOCOLLO_NOSTALE.md` — protocol reference for client-observable traffic.
- `docs/RECOVERY_WATCHDOG.md` — recovery/watchdog constraints.
- `docs/research/` — dated research evidence; research never overrides an ADR or canonical specification.

## Multi-agent operating rule

A1–A5 may work in parallel only on disjoint ownership sets. A6 integrates only after complete handoffs. Agents must write complete files and may not introduce TODO/FIXME/pseudocode/stubs/partial implementations. No phase command authorizes deletion of existing project files. `third_party/` is not a deletion target.

## Architecture and execution rule

The product target is an autonomous player. Mouse and keyboard are permitted but optional. PC resources and non-privileged client-observable software interfaces may be used. No server/admin/GM information or external automation hardware may enter the gameplay truth/control path.

The canonical execution chain remains:

`Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking → Orchestrator → Planner → Guard → Trust → Safety → Execute → Verify → Re-observe`

Presence of code or documentation never means `Verified`. Verification requires the evidence defined by the applicable roadmap phase.
