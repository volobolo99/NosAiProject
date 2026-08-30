# NosAiProject — Source of Truth Index

**Version:** 1.0
**Date:** 2026-08-30
**Status:** ACTIVE
**Milestone:** `M003`

This index lists the documents that are authoritative for implementation, review and verification. When two documents disagree, stop and resolve the conflict with an ADR rather than inventing a third behavior.

## Canonical documents

| Rank | Document | Authority |
|---|---|---|
| 1 | `docs/NOSAI_ARCHITECTURE_BASELINE.md` | Layer boundaries, data classification, trust boundaries |
| 2 | `docs/adr/*.md` | Accepted architectural decisions |
| 3 | `NOSAI_MASTER_ROADMAP.md` | Milestone order and verification language |
| 4 | `docs/ROADMAP.md` | Operational Gate 1 priority and phase gates |
| 5 | `docs/GATE1_CHECKLIST.md` | Executable Gate 1 acceptance points |
| 6 | `docs/GATE1_COMPONENT_MAP.md` | Current Gate 1 component maturity |
| 7 | `docs/BUILD_TEST_RELEASE.md` | Reproducible build/test/release commands |
| 8 | `docs/GIT_WORKFLOW.md` | Branch, commit and review workflow |
| 9 | `CLAUDE.md` | Agent implementation rules |
| 10 | `.cursor/rules/*.mdc` | Cursor project rules |

## Accepted ADRs

- `ADR-0001` — canonical layered architecture
- `ADR-0002` — real/demo data separation (`LIVE`, `DERIVED`, `CACHED`, `SIMULATED`, `UNKNOWN`)
- `ADR-0003` — runtime safety authority
- `ADR-0004` — verification before release
- `ADR-0005` — versioned contracts

## Gate 1 contract

The first operational circuit is:

`PC runtime bootstrap → real client connection → minimum classified data → authenticated Guard AI smartphone → coherent dashboard → error/disconnect handling`

The canonical Gate 1 snapshot is version `gate1.snapshot.v1`. Unknown values must be emitted as `UNKNOWN` with a null value. Zero, false and empty are not substitutes for unknown.

## Non-authoritative by themselves

File presence, local mocks, later gates (4/5/6) and dashboard chrome do not make a milestone `VERIFIED`. Supporting documents in `docs/` remain valid for domain context but cannot override the documents listed above.
