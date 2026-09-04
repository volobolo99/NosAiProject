# NosAiProject — Source of Truth

**Version:** 2.0  
**Date:** 2026-09-05  
**Status:** ACTIVE

This file defines which project documents are authoritative. When documents disagree, stop and resolve the conflict with an ADR.

## Canonical documents

1. `docs/ROADMAP_ESECUTIVA.md` — canonical development order and gates.
2. `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` — product capabilities, autonomy target and non-privileged boundary.
3. `docs/NOSAI_ARCHITECTURE_BASELINE.md` — layer boundaries, trust boundaries and architectural invariants.
4. `docs/UNPRIVILEGED_DEMO_SPEC.md` — reproducibility and permitted observation/control boundary.
5. `docs/adr/*.md` — accepted architectural decisions; later accepted ADRs override earlier decisions where explicitly stated.
6. `CLAUDE.md` and `.cursor/rules/*.mdc` — agent development rules.
7. `third_party/README.md`, `third_party/manifests/*`, `third_party/provenance/*` — external-source provenance and reuse rules.

## Development-support documents

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

## Architecture and execution rule

The product target is an autonomous player. Mouse and keyboard are permitted but optional. PC resources and non-privileged client-observable software interfaces may be used. No server/admin/GM information or external automation hardware may enter the gameplay truth/control path.

The canonical execution chain remains:

`Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking → Orchestrator → Planner → Guard → Trust → Safety → Execute → Verify → Re-observe`

Presence of code or documentation never means `Verified`. Verification requires the evidence defined by the applicable roadmap phase.
