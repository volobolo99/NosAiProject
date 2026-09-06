# AP-04 / A2+A4 — DeepSeek — Wire `PortalCrossingDetector` into `--scout`/`--autoplay`

## Why this task exists

`AP-04_A1_STATUS.md` §"Seconda indagine sui portali" found a real,
buildable source of portal data this repository never had before:
**observe while crossing**. No client file or table names a portal's
destination anywhere in this repository (verified again this session);
`ClientMemorySession.TryReadMapId`/`TryReadPlayer` are already real,
already proven, and already polled every round by `--scout`/`--autoplay`
— a map-id change between two consecutive polls is an honest, observable
fact, not a guess. `src/NosAi.Core/WorldModel/Reconstruction/PortalCrossingDetector.cs`
(already on `main`, Claude/A1+A3) turns that pair of readings into a real
`Portal` when a crossing happens. **This task is the wiring that turns
that pure algorithm into rows in the persisted map model**: one new
method on the already-real `MapReconstructionSource` (A2), and one
additive polling hook in each of `ScoutCommand.RunWindows`/
`AutoplayCommand.RunWindows` (A4) — no new command, no new flag.

**What this buys, and what it deliberately does not.** Once wired, a real
operator run of `--scout`/`--autoplay` that crosses a map boundary
persists a `Portal` fact for the map it left — the same field
(`MapObservationBatch.Portals`/`MapModel.Portals`) `WorldMapPortalRouter`
would need real data for, finally populated by something other than
`EquatableArray<Portal>.Empty`. It does **not** wire that portal data
into `WorldMapPortalRouter`'s own multi-map routing, and it does **not**
give `NavigationPlan` a second waypoint — both remain separate, later
decisions once enough real portal data exists to route through. This
task only makes the data start accumulating.

## Already built and real — read before writing code

- `src/NosAi.Core/WorldModel/Reconstruction/PortalCrossingDetector.cs`
  (already on `main`): `MapPositionReading(MapId, WorldPosition, DateTime)`,
  `PortalCrossingDetector.DetectCrossing(MapPositionReading previous, MapPositionReading current, double confidence = 1.0) -> Portal?`.
  Read it and its own tests
  (`tests/NosAi.Core.Tests/WorldModel/Reconstruction/PortalCrossingDetectorTests.cs`)
  in full. Do not change this file: it is pure and already covered.
- `src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs`: read
  `Resolve` and every private helper it calls
  (`LoadPersistedOrUnknown`, `PersistIfPossible`, the `_cachedMapId`/
  `_cachedResult` fields) in full. §1 below adds one new public method
  that reuses these exact helpers — do not duplicate their logic.
- `src/NosAi.Core/WorldModel/Reconstruction/MapObservationBatch.cs`/
  `MapReconstructionFusion.cs`: `MapObservationBatch.Portals` already
  exists and `MergeByKey(current.Portals, batch.Portals, portal => portal.Id)`
  already merges by `Portal.Id` (refines a repeated observation of the
  same id instead of duplicating it) — this is why
  `PortalCrossingDetector`'s id is derived from a rounded position, not a
  fresh `Guid` per crossing. Do not change either file.
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs`/
  `src/NosAi.Runtime/Tactical/AutoplayCommand.cs`, `RunWindows`: both
  already poll `TryReadMapId`/`TryReadPlayer` every round/cycle and
  already reset `footprint` when `currentMapId` changes from the previous
  round — read both `RunWindows` methods in full before touching either.
  §2 below adds a second, independent piece of per-round state
  (`previousReading`) next to the existing `footprint` local; it does not
  reuse or repurpose `footprint` for this.
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionSourceTests.cs`:
  read in full, including its `TestVolume`/`TempMapsDir`/`SnapshotFor`
  private helpers — §1's tests reuse all three, do not rebuild them.

## OWN (new files only)

None. Every change in this task is additive inside existing files.

## MODIFY

- `src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs` (one new public method)
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs` (`RunWindows` only)
- `src/NosAi.Runtime/Tactical/AutoplayCommand.cs` (`RunWindows` only)
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionSourceTests.cs` (new tests only)

Do not touch `PortalCrossingDetector.cs`, `MapObservationBatch.cs`,
`MapReconstructionFusion.cs`, `CollectCommand.cs`, `EngageCommand.cs`,
`RecoverCommand.cs`, `WalkCommand.cs`, or any `ExecuteOneRound`/
`ExecuteOneCycle` signature in `ScoutCommand.cs`/`AutoplayCommand.cs`.

## Scope, stated explicitly

- Covers exactly two commands' round loops: `--scout` and `--autoplay`'s
  Exploration dispatch (which reads map id/player position in its own
  `RunWindows` loop, independent of `--scout`'s). Both already poll map
  id every round; no new polling infrastructure is introduced.
- **`--collect`/`--engage`/`--recover` are deliberately out of scope.**
  None of them re-reads `TryReadMapId` on every round the way `--scout`/
  `--autoplay` do (`--collect` reads it once before its round loop, the
  other two never read it at all) — wiring crossing detection there would
  require adding new per-round polling these commands do not have today,
  a materially different and larger change than this task's "reuse
  exactly what already gets polled."
- `WorldMapPortalRouter`'s own multi-map routing is **not** touched —
  recording real portal data and consuming it for routing are separate
  decisions; this task only does the former.
- Recording is **best-effort, never a gate**: `RecordPortalCrossing`
  (§1) reuses `PersistIfPossible`, which already swallows and logs a
  store failure rather than throwing — a portal that fails to persist
  must never make `--scout`/`--autoplay` refuse or abandon a round whose
  actual walk already succeeded.

## 1. `MapReconstructionSource.RecordPortalCrossing` (A2)

Add as a new public method, after `Resolve` and before `Dispose`:

```csharp
    /// <summary>
    /// Merges one freshly-detected <see cref="Portal"/> into the map it was
    /// observed on (<see cref="Portal.SourceMap"/>) -- independent of
    /// whichever map <see cref="Resolve"/> is resolving this same cycle. A
    /// crossing is detected on the cycle where the player has already
    /// arrived on the destination map, so by the time a caller calls this
    /// method, this same cycle's own <see cref="Resolve"/> call (if any)
    /// carries the destination map's id, never the source map's -- this
    /// method is the only way to record evidence for a map the caller has
    /// already left.
    /// </summary>
    /// <param name="portal">
    /// The portal to merge, exactly as
    /// <see cref="NosAi.Core.WorldModel.Reconstruction.PortalCrossingDetector.DetectCrossing"/>
    /// already derived it. This method never re-derives or second-guesses it.
    /// </param>
    /// <param name="nowUtc">The instant this call runs at.</param>
    public void RecordPortalCrossing(Portal portal, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(portal);
        ObjectDisposedException.ThrowIf(_disposed, this);

        MapId sourceMap = portal.SourceMap;
        MapModel baseline = _cachedMapId is { } cachedId && cachedId.Equals(sourceMap) && _cachedResult is { } cached
            ? cached
            : LoadPersistedOrUnknown(sourceMap, nowUtc);

        var batch = new MapObservationBatch(
            sourceMap,
            EquatableArray<Tile>.Empty,
            EquatableArray<Portal>.From(new[] { portal }),
            "portal_crossing_observed",
            nowUtc);

        MapModel merged = MapReconstructionFusion.Merge(baseline, batch, nowUtc);
        PersistIfPossible(merged);

        // Only refresh the in-memory cache when it still belongs to the same
        // map this portal was observed on -- Resolve's own cache for
        // whichever map the caller is CURRENTLY on (the destination) must
        // never be overwritten by a fact about the map the caller just left.
        if (_cachedMapId is { } cid && cid.Equals(sourceMap))
            _cachedResult = merged;
    }
```

**Test** (add to `MapReconstructionSourceTests.cs`, reusing its existing
`TestVolume`/`TempMapsDir`/`SnapshotFor` helpers — no new test
infrastructure):

- Recording a portal for a map that was never `Resolve`d this session
  persists it: construct a fresh `MapReconstructionSource`, call
  `RecordPortalCrossing` directly (no prior `Resolve` call), then open a
  **second** `MapReconstructionSource` against the same `TestVolume`
  options and confirm `Resolve` (or a direct `MapModelStore.TryLoad`, if
  simpler) returns a `MapModel` whose `Portals` contains exactly that
  portal.
- Recording a portal for the **currently cached** map (call `Resolve`
  for map A, then `RecordPortalCrossing` for a portal whose `SourceMap`
  is also map A) updates the in-memory cache: after recording, delete
  the `.grid` file (or point `mapsDirectoryOverride` at an empty
  directory) and call `Resolve` again for map A — it must return the
  portal-bearing result from cache, not fail or fall back to an empty
  grid re-projection (proving the cache path was taken, not a re-read).
- Recording a portal whose `SourceMap` is **not** the currently cached
  map leaves that cache untouched: `Resolve` map B, `RecordPortalCrossing`
  for a portal on map A, then `Resolve` map B again — same result as
  before (the second `Resolve` call for B must not have re-run the grid
  pipeline just because a portal was recorded elsewhere; assert equality
  with the first call's result, or use the same "delete the grid file"
  trick as above if a positive assertion is easier than a negative one).
- Two `RecordPortalCrossing` calls with `Portal`s sharing the same `Id`
  (same rounded source position, as two crossings of the same physical
  portal would produce) merge into **one** row, not two: assert
  `result.Portals.Count == 1` after both calls, with the second call's
  `DestinationMap`/timestamps reflected (mirrors `MapReconstructionFusion`'s
  own already-tested `MergeByKey` behavior — this test proves the
  end-to-end wiring preserves it, not that `MergeByKey` itself works).

## 2. Wiring `ScoutCommand.RunWindows`

Immediately after this existing line:

```csharp
            ExplorationFootprint footprint = ExplorationFootprint.Empty(
                new MapId("unknown-map"), "scout_command_session_start");
```

insert:

```csharp

            // Tracks the previous round's (map, position) reading so a map
            // change between two consecutive rounds can be recognised as a
            // portal crossing (PortalCrossingDetector) -- null before the
            // first round runs. Independent of `footprint`: this is about
            // recording a fact for the map just left, not the one just
            // entered.
            MapPositionReading? previousReading = null;
```

Inside the `for (int round = 1; round <= rounds; round++)` loop, change:

```csharp
                DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
                var currentMapId = new MapId(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"map-{mapId}"));
                if (!footprint.MapId.Equals(currentMapId))
                {
                    footprint = ExplorationFootprint.Empty(currentMapId, "scout_command_session_start", now);
                }
```

to:

```csharp
                DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
                var currentMapId = new MapId(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"map-{mapId}"));

                var currentReading = new MapPositionReading(currentMapId, new WorldPosition(player.X, player.Y), now);
                if (previousReading is { } previous)
                {
                    Portal? crossing = PortalCrossingDetector.DetectCrossing(previous, currentReading);
                    if (crossing is not null)
                        mapReconstruction.RecordPortalCrossing(crossing, now);
                }
                previousReading = currentReading;

                if (!footprint.MapId.Equals(currentMapId))
                {
                    footprint = ExplorationFootprint.Empty(currentMapId, "scout_command_session_start", now);
                }
```

Add `using NosAi.Core.WorldModel.Reconstruction;` to this file's using
list (for `MapPositionReading`/`PortalCrossingDetector`) — `Portal`/
`WorldPosition` are already covered by the existing
`using NosAi.Core.WorldModel;`.

## 3. Wiring `AutoplayCommand.RunWindows`

Same two changes, same anchors, same reasoning as §2:

1. Immediately after the existing
   `ExplorationFootprint footprint = ExplorationFootprint.Empty(new MapId("unknown-map"), "autoplay_command_session_start");`,
   insert the identical `MapPositionReading? previousReading = null;`
   declaration (same comment) from §2.
2. Inside the `for (int cycle = 1; cycle <= cycles; cycle++)` loop,
   change:

```csharp
                DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
                var currentMapId = new MapId(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"map-{mapId}"));
                if (!footprint.MapId.Equals(currentMapId))
                {
                    footprint = ExplorationFootprint.Empty(currentMapId, "autoplay_command_session_start", now);
                }
```

   to the same shape §2 shows (insert the `currentReading`/crossing-check/
   `previousReading = currentReading;` block between computing
   `currentMapId` and the existing footprint-reset `if`).

Add `using NosAi.Core.WorldModel.Reconstruction;` to this file's using
list as well.

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~MapReconstructionSourceTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

All green, 0 warnings/0 errors, no regression in either full suite. No
existing test in `ScoutCommandTests.cs` (if it exists)/`AutoplayCommandTests.cs`
should need to change — §2/§3 only touch the untested `RunWindows`
shells, same as every prior wiring task this session.

## Known limitation, stated plainly

`RunWindows` in both files has no unit test by design (same convention
`WalkCommand.RunWindows` already documents) — the actual crossing
detection call inside the loop is therefore verified by (a) a clean
build, (b) `PortalCrossingDetectorTests`/the new
`MapReconstructionSourceTests` proving the two real units this wiring
calls are correct in isolation and in the exact merge/cache shape this
task relies on, and (c) no regression in the full suites. It is **not**
verified end-to-end against a real client crossing a real portal in this
task — report `Present`, `Integrated` only once a human confirms a real
`--scout`/`--autoplay` run that crosses a map boundary actually leaves a
new row in the persisted map model's `Portals`.

## Report back

Files modified; build/test evidence with exact pass counts;
verification level (`Present`) and why not `Integrated`; anything found
in `MapReconstructionSource.cs`/`PortalCrossingDetector.cs`/either
`RunWindows` method that looks wrong (do not fix it yourself — report
it).
