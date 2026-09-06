# ADR-0026 — `DataSourceKind` is intentionally declared three times, one per bounded context

**Status:** Accepted
**Date:** 2026-09-06

## Context

`DataSourceKind` (`Live`/`Derived`/`Cached`/`Simulated`/`Unknown`, the provenance every observed fact in this project carries) is declared three times, identical by value and name in each case:

1. `src/NosAi.Core/Hardware/HardwareClassification.cs` — paired with `ClassifiedValue<T>`, the hardware-capability/scheduling domain (AP-00).
2. `src/NosAi.Core/WorldModel/WorldModelClassification.cs` — paired with `WorldFact<T>`, the gameplay World Model domain (AP-01 onward).
3. `src/NosAi.Runtime/Contracts/DataClassification.cs` — paired with `ClassifiedValue<T>` again, the pre-existing Gate 1-6 runtime domain.

This was flagged as an open gap by the AP-01/A5 audit and referenced again by `docs/agents/phases/AP-09/AP-09_A1_STATUS.md` when the same pattern was found for `KnowledgeScope` (resolved separately — see that document's "Correzione di contratto"-style entry for this task). `WorldModelClassification.cs`'s own doc comment already argued the duplication is deliberate: *"Re-declared here, in the World Model's own bounded context, rather than reused from `NosAi.Core.Hardware`: that namespace is a hardware capability/scheduling concern..., and importing it from Player/Quest/Mob contracts would tie two unrelated domains together for no architectural reason."* This ADR verifies that argument against the actual codebase rather than accepting it at face value, and records the outcome so the same gap is not re-flagged by a future audit without first reading this decision.

### Findings

- The three declarations are identical by value (`Live=0, Derived=1, Cached=2, Simulated=3, Unknown=4`) — not already-diverged copies. Their **wrapper types** differ on purpose: `WorldFact<T>` (World Model) carries a continuous `Confidence` alongside the categorical source, which `ClassifiedValue<T>` (Hardware and Runtime.Contracts) does not — gameplay sensor fusion routinely produces disagreement between sources, hardware capability reads generally do not.
- `src/NosAi.Core/NosAi.Core.csproj` has zero `ProjectReference`/`PackageReference` entries — confirmed, not assumed. `NosAi.Core.Hardware` and `NosAi.Core.WorldModel` therefore cannot reference a type declared in `NosAi.Runtime.Contracts` even if it were architecturally desirable; the dependency can only run the other way (`NosAi.Runtime` → `NosAi.Core`).
- Two real conversion bridges already exist exactly where the three domains actually meet, and both are correct, tested and documented — this duplication has already been mitigated at its integration points, not left as raw friction:
  - `ClassifiedValueBridge.WithSource` (`src/NosAi.Runtime/WorldModel/Fusion/ClassifiedValueBridge.cs`) converts Runtime.Contracts' `DataSourceKind` into a `WorldFact<T>`.
  - `RuntimeHardwareCapabilityProvider.Convert<T>` (`src/NosAi.Runtime/Hardware/Gate/RuntimeHardwareCapabilityProvider.cs`) converts Runtime.Contracts' `DataSourceKind` into `NosAi.Core.Hardware`'s `ClassifiedValue<T>`, with its own comment already citing the same zero-dependency constraint as the reason a shared type cannot live in `NosAi.Runtime.Contracts`.
  - No bridge exists between Hardware and WorldModel directly — the two domains never touch each other in this codebase, consistent with them being genuinely separate bounded contexts, not two views of the same data.
- Consumer count is large and asymmetric: `NosAi.Core.Hardware`'s `DataSourceKind` has roughly a dozen consumers; `NosAi.Core.WorldModel`'s `WorldFact<T>` is referenced by on the order of 80 files (every gameplay contract from AP-01 onward, plus their tests); `NosAi.Runtime.Contracts`' `DataSourceKind` is referenced by roughly 120 files across the entire pre-existing Gate 1-6 runtime plus `NosAi.ControlPanel`.

## Decision

**The triple declaration stands. No code changes.** A canonical shared enum is technically constructible (`NosAi.Runtime` is free to depend on a new type in `NosAi.Core`), but the only benefit would be deleting the two small, already-correct conversion switches above — a few dozen lines — at the cost of touching roughly 200 files across three assemblies that already work, rewriting the doc comments that currently and accurately explain the separation, and re-verifying three assemblies' test suites for a change with no functional effect. That is exactly the kind of broad, low-value touch this project's own contributing rules warn against.

This ADR formally closes the AP-01/A5 finding: the duplication is a deliberate consequence of `NosAi.Core` having zero dependencies and hosting more than one bounded context (`Hardware`, `WorldModel`) that must not import each other, not an oversight. A future audit that re-discovers these three declarations should cite this ADR rather than re-opening the question from scratch.

## Consequences

- No source file changes as a result of this decision.
- A future domain that also needs a Live/Derived/Cached/Simulated/Unknown provenance concept should default to re-declaring it locally in its own bounded context (mirroring `WorldModelClassification.cs`'s existing doc comment), not reaching into one of the three existing declarations across a domain boundary — unless that future work also introduces a real, justified reason to merge two of the three domains, which would need its own ADR.
- If `NosAi.Core.Hardware` and `NosAi.Core.WorldModel` ever need to exchange provenance directly (no such need exists today), the correct fix is a fourth, neutral declaration in a shared, domain-agnostic location within `NosAi.Core`, mirrored by both — not collapsing either existing one into the other.
