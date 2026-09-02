# NosAiProject — Source of Truth Index

**Version:** 1.0
**Date:** 2026-08-30
**Status:** ACTIVE
**Milestone:** `M003`

This index lists the documents that are authoritative for implementation, review and verification. When two documents disagree, stop and resolve the conflict with an ADR rather than inventing a third behavior.

`docs/NOSAI_ARCHITECTURE_BASELINE.md` is titled "Architecture Baseline & Source of Truth" and the two names have caused confusion: it is the authority on *what the architecture is*, while this file is the authority on *which document wins* when two of them disagree. They are complementary, and neither is a copy of the other.

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
- `ADR-0006` — canonical PC↔phone channel (NOSA framing, RSA-2048, TCP/17471)
- `ADR-0007` — Wi-Fi transport and LAN discovery (UDP/17472, `NOSD`)
- `ADR-0008` — mutual handshake (wire version 2, signed session transcript)
- `ADR-0009` — authenticated encryption of the session payload (wire version 3)
- `ADR-0010` — custody of the long-term identity keys (DPAPI on the PC, Android Keystore on the phone; **PC verified locally, phone not yet exercised on a device**)
- `ADR-0011` — who may hold the single Guard session (bounded concurrent admission, first to authenticate wins)
- `ADR-0012` — comparison of gameplay observation sources and their failure modes (amended by ADR-0014; traffic capture and process memory reads are now implemented, **no gameplay provider is wired to a real client**)
- `ADR-0013` — **superseded by ADR-0014**, kept for the record
- `ADR-0014` — the operator chooses the data path: traffic capture, memory reads and client control are available; Safety and classification still bind
- `ADR-0015` — adopt `ROADMAP_ESECUTIVA` as the canonical architecture
- `ADR-0016` — planning and acting on partial observation: the planner skips every rule that reads an `UNKNOWN` fact
- `ADR-0017` — the wire teaches the screen to read: network readings label the HUD glyph atlas
- `ADR-0018` — the screen establishes `HasTarget`, the wire confirms it and never creates it
- `ADR-0019` — operating system input is the actuation channel for character control (scopes a choice ADR-0014 left open; revokes nothing)
- `ADR-0020` — **Proposed.** One authorisation path to the act: the commit point is the boundary, the token signs the intent digest instead of the `CandidateId` and reaches the effector. Supersedes `ROADMAP_ESECUTIVA` §§ 8.2–8.3 for actuation and narrows `INV-07` to the transport codec, which is why it awaits acceptance

## Gate 1 contract

The first operational circuit is:

`PC runtime bootstrap → real client connection → minimum classified data → authenticated Guard AI smartphone → coherent dashboard → error/disconnect handling`

The canonical Gate 1 snapshot is version `gate1.snapshot.v1`. Unknown values must be emitted as `UNKNOWN` with a null value. Zero, false and empty are not substitutes for unknown.

`safety.executionMode` is **derived** from the live switch state (`enabled_by_operator` / `disabled_by_operator`) and is no longer the fixed literal `disabled_in_gate1`. Execution, live input and packet injection are operator switches, off at start, changeable only by `SecurityPrincipal.Operator`, and every change is recorded with its before/after value and reason.

## Character control

Three supporting documents cover the control of the character inside the client. They are
subordinate to `NOSAI_ARCHITECTURE_BASELINE.md` and to the ADRs, and they say so.

| Document | Covers |
|---|---|
| `docs/CONTROLLO_PERSONAGGIO_ARCHITETTURA.md` | Domain boundary, actuation channel, `DOMAIN-xx` invariants |
| `docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md` | Commit point, occlusion, human precedence, map grid, verification windows |
| `docs/CONTROLLO_PERSONAGGIO_ROADMAP.md` | Order of work and the Claude / Cursor split |

## Non-authoritative by themselves

File presence, local mocks, later gates (4/5/6) and dashboard chrome do not make a milestone `VERIFIED`. Supporting documents in `docs/` remain valid for domain context but cannot override the documents listed above.
