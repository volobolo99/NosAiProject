# AP-03 / A5 — Claude — Independent Audit

Empirically audit AP-03 (Map Reconstruction) end to end: A1
(`MapObservationBatch`), A2 (`MapGridObservationProjector`), A3
(`MapReconstructionFusion.Merge`), A4 (`MapModelStore`,
`MapReconstructionSource`, the `WorldModelFusionLoop`/`Program.cs` wiring).
Do not trust any prior agent's self-report, including this phase's own A4
completion notes — verify by reading the actual diff and by writing new
tests that try to break it, the same standard AP-01/A5 and AP-02/A5 already
applied in this repository (both found real defects this way; assume this
phase can hide one too).

READ: `docs/ROADMAP_ESECUTIVA.md` S:AP-03; `docs/agents/phases/AP-03/*.md`;
every file A1-A4 created or modified (`git log --stat` since the AP-02
integration commit, or `git diff` against `fcae360` if that is still the
last pre-AP-03 commit, will show you the exact set).

OWN: new test files only, plus
`docs/agents/phases/AP-03/AP-03_A5_AUDIT.md` (your findings report, same
format as `docs/agents/phases/AP-01/AP-01_A5_AUDIT.md` and
`docs/agents/phases/AP-02/AP-02_A5_AUDIT.md` — read one of those for the
expected structure/tone before writing yours). Do not modify any A1-A4
source file yourself, even if you find a bug — report it with enough
detail (file, line, failing input, expected vs actual) that A6 can fix it
in one pass. Do not create a git commit.

## Specific things to hunt for (informed by this project's own history — every one of these has been a real, previously-undiscovered defect in a prior phase, not a hypothetical)

1. **The wall-clock leak.** Any `WorldFact<T>.Unknown(...)` /
   `MapObservationBatch.Empty(...)` / similar factory call anywhere in A1-A4
   that omits the optional instant parameter when the surrounding method
   already received one, silently falling back to `DateTime.UtcNow` and
   breaking determinism/replay. This exact bug has been found and fixed six
   separate times across AP-00/AP-01/AP-02 (see
   `docs/agents/phases/AP-02/AP-02_A5_AUDIT.md` §7 for the running list) —
   check every single `Unknown`/`Empty` call site in the new files, not just
   a sample.
2. **`MapReconstructionSource`'s caching claim.** A4's command file
   (`AP-03_A4_CLAUDE_persistence_and_wiring.md`) required `Resolve` to cache
   its result per map id and skip re-touching the grid file and the SQLite
   store on a repeated call for the same map. Verify this is actually true
   by test, not by reading the code and assuming it does what it says:
   construct a source, resolve once, then make the grid directory
   unreadable (delete it / rename it) and resolve again for the *same* map
   id — if it still succeeds using the cached result, the claim holds; if it
   degrades to Unknown/Empty, the caching is not actually short-circuiting
   the I/O path and this is a real (performance, not just correctness) bug.
3. **`MapId` round-tripping.** The `"map-{numericId}"` <-> `int` parser A4
   had to write by hand: check its behavior on `"unknown-map"`, an empty
   string, a non-numeric suffix, a negative number, and a numeric suffix
   with leading zeros or overflow past `int.MaxValue` — confirm every case
   fails closed (no real map id assumed) rather than throwing or silently
   parsing garbage.
4. **`MapReconstructionFusion.Merge`'s idempotence under real A2 output.**
   A3's own tests use small hand-built batches; A2's projector produces one
   `Tile` per grid cell, which for even a modest map is thousands of tiles.
   Confirm merging the *same* full-grid batch twice in a row still returns
   the identical `MapModel` reference (no version bump) at that scale, not
   just for the 1-2-tile examples in A3's unit tests -- an O(n) or dictionary
   correctness issue could plausibly only surface at real size.
5. **`MapModelStore` round-trip fidelity.** Confirm every field survives a
   save/load cycle with its full classification intact --
   `WorldFact<T>.Source`/`Confidence`/`ObservedAtUtc`/`HasObservedValue`/
   `Reason` for tiles, portals and bounds, not just the bare values. Also
   confirm saving twice for the same `MapId` overwrites rather than
   accumulating duplicate rows (query the table directly if needed).
6. **The `MapModel.Version` semantics change.** Before AP-03, every
   `MapModel` produced by `GameplayObservationProjector` reused the global
   fusion-cycle `version` counter for `MapModel.Version` (see
   `GameplayObservationProjector.cs` line ~97). After A4's wiring, the
   `Map` field gets fully replaced by `MapReconstructionSource.Resolve`'s
   own result, whose `Version` is now the map-specific counter
   `MapReconstructionFusion.Merge` maintains. Confirm nothing downstream
   (existing tests, `WorldModelSnapshot` consumers) silently assumed the old
   coupling between the two version numbers -- grep for
   `.Map.Version` / `snapshot.Version` compared against each other anywhere
   in the codebase.
7. **Exception safety at every new I/O boundary** (`MapModelStore`
   construction, `MapGridExtractor.TryInfo`, the maps-directory resolution)
   -- confirm `MapReconstructionSource.Resolve` and the
   `WorldModelFusionLoop` wiring around `_mapSource` truly never throw out
   to the caller, by test (inject a source/store that throws and confirm the
   cycle still completes with the map field merely unchanged), not by
   inspection alone.
8. **Determinism of the full pipeline.** Two independent
   `MapGridObservationProjector.Project` + `MapReconstructionFusion.Merge`
   runs against the same grid bytes and the same instant must produce
   `MapModel`s that compare equal with `Assert.Equal`, end to end, not just
   at each stage in isolation.

Also verify, without necessarily finding a defect: `EquatableArray<Tile>`
equality actually holds at grid scale (thousands of elements) and is not
merely correct for the handful of elements A1/A3's own tests use;
`NosAi.Core` still has zero dependency on `NosAi.Runtime` (A1's contracts
must not have grown one); the new `NosAi.Storage` type does not weaken any
existing `SqliteEventJournal`/`VolumeLocator` behavior (you are not supposed
to have touched those files, but confirm A4 in fact didn't).

## Build/test evidence required

```
export PATH="$PATH:/root/.dotnet"
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
dotnet build src/NosAi.Storage/NosAi.Storage.csproj -c Release
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
```

Report exact pass/fail counts for both full suites (not just your new
tests), plus the counts filtered to `~Reconstruction|~MapModelStore|~MapGrid`
to isolate AP-03-specific coverage. Every finding must be reproduced by a
new failing test committed to your own test files (not merely described in
prose) before it goes in the report, exactly like the AP-01/AP-02 audits
did.

## Report format (`AP-03_A5_AUDIT.md`)

Mirror `AP-02_A5_AUDIT.md`'s structure: scope recap, per-item findings
(numbered, each with file/line, the failing test that proves it, and the
concrete fix A6 should apply), a final "verified without finding a defect"
section listing what you checked and did not break, and exact build/test
evidence. State plainly whether this phase is ready for A6 integration or
whether something blocks it outright.
