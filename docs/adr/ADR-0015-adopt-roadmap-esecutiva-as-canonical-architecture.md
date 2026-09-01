# ADR-0015 — Adopt `docs/ROADMAP_ESECUTIVA.md` as the canonical architecture and roadmap

**Status:** Accepted
**Date:** 2026-09-01

## Context

`docs/ROADMAP_ESECUTIVA.md` ("Roadmap Esecutiva — NosAi 1.0 Beta", revision 31 agosto 2026) was introduced as a new, self-contained specification: a fixed critical path (`Observe -> WorldState -> Simulation -> Ranking -> Orchestrator -> Planner -> Guard -> Trust -> Safety -> Execute -> Verify`), eight non-negotiable invariants (INV-01..INV-08), a hard project dependency graph (`NosAi.Core`, `NosAi.Perception`, `NosAi.Adapter`, `NosAi.Security`, `NosAi.Storage`, `NosAi.Host`), and eight sequential Gates each with a binding Definition of Done (build, tests, allocation benchmark, latency budget, journal integrity, negative tests, signed human validation).

This conflicts directly with the architecture recorded in `docs/NOSAI_ARCHITECTURE_BASELINE.md`, `CLAUDE.md`, `.cursor/rules/00-nosai-core.mdc` and `NOSAI_MASTER_ROADMAP.md`, all of which describe a different canonical flow (`Observe → World Model → Decision/Policy → Safety → Execute → Verify → Re-observe`) and a different project layout (`NosAi.Runtime`, `NosAi.Protocol`, `NosAi.GuardClient`, `NosAi.ControlPanel`, `NosAi.GuardAi.App`). The existing `WireHeader` (`src/NosAi.Protocol/WireProtocol.cs`) and the new `NosFrameHeader` are both 12-byte structures but are laid out differently (magic+version+type+length+sequence vs. version+opcode+length+sequence+HMAC tag) and are not interchangeable.

Per `docs/NOSAI_ARCHITECTURE_BASELINE.md` §11 ("Change management") and `CLAUDE.md` ("Stop on architectural contradictions instead of inventing undocumented behavior"), this conflict must be resolved by an explicit decision rather than by silent drift in either direction.

The operator was asked directly which of the following applied: (a) `ROADMAP_ESECUTIVA.md` is the new authoritative architecture, superseding the existing one; (b) it is a vision document to reconcile with the existing baseline before any implementation; (c) it is archived as future reference while incremental work continues on the existing architecture. The operator selected **(a)**.

## Decision

`docs/ROADMAP_ESECUTIVA.md` is now the canonical architecture and execution plan for NosAiProject 1.0 Beta, superseding the flow and project layout in `docs/NOSAI_ARCHITECTURE_BASELINE.md` and `NOSAI_MASTER_ROADMAP.md` for all new work.

Consequences of this decision, stated explicitly so they are not rediscovered by accident later:

1. **New project set.** `NosAi.Core`, `NosAi.Perception`, `NosAi.Adapter`, `NosAi.Security`, `NosAi.Storage`, `NosAi.Host` are added to the solution per the dependency graph in `docs/ROADMAP_ESECUTIVA.md` §1.3 (`Core` referenced by everything, referencing nothing; no back-references, no cycles).
2. **The existing runtime is not deleted.** `NosAi.Runtime`, `NosAi.Protocol`, `NosAi.GuardClient`, `NosAi.ControlPanel`, `NosAi.GuardAi.App` and their tests keep building and keep passing; they are not wired into the new Gate path and are not extended further under the old architecture except for maintenance. Removing ~500 passing tests and a working Gate 1 (old model) end-to-end path to make room for an unproven rewrite would trade a known-working system for an unverified one; the two coexist until the new path reaches parity and is certified, at which point a follow-up ADR records the cut-over or the deprecation.
3. **The wire protocol is not shared.** `NosFrameHeader` (`NosAi.Security`) is a new, independent 12-byte frame format for the new path. It does not replace, version, or interoperate with `WireHeader` (`NosAi.Protocol`). The two are documented as distinct protocols so neither is mistaken for a revision of the other.
4. **`docs/NOSAI_ARCHITECTURE_BASELINE.md` and `NOSAI_MASTER_ROADMAP.md` are marked superseded**, not deleted: they remain the accurate description of `NosAi.Runtime`'s own architecture and milestones, which is still real, running code.
5. **Physical/hardware validation stays deferred.** Every Gate in `docs/ROADMAP_ESECUTIVA.md` ends in a human-in-the-loop signature on real NosTale, a real mobile Guard node, and (from Gate 6) real GPU thermal stress. None of that hardware is available in an agent session; those checklist items are tracked in `docs/TEST_RIMANDATI.md` exactly like the equivalent items for the old Gate 1, and are never claimed `VERIFIED` from local test runs alone.
6. **New third-party dependencies this path requires** (justified individually, pinned exact versions, no floating ranges): `Noise.NET` (Noise Protocol Framework, `NosAi.Security` only — implementing a Noise handshake by hand instead of using a maintained, spec-conformant library would be introducing hand-rolled cryptography for no benefit); `Microsoft.Data.Sqlite` (already a dependency of `NosAi.Runtime`; reused, not reintroduced, in `NosAi.Storage`).

## Consequences

- Two architectures now exist side by side in one repository for a transition period. Anyone reading `CLAUDE.md`/`00-nosai-core.mdc`/`NOSAI_MASTER_ROADMAP.md` without also reading this ADR and `docs/ROADMAP_ESECUTIVA.md` will see an incomplete picture; those documents will be annotated to point here.
- Work under the new path follows `docs/ROADMAP_ESECUTIVA.md` §1.4 (Definition of Done) and §10 (Gate transition rule): Gate `N+1` is not started, even in scaffolding, until Gate `N`'s eight DoD items are green and signed.
- Every Gate's physical human-in-the-loop item is a permanent, tracked gap until the operator runs it for real; this ADR does not shorten that requirement.
